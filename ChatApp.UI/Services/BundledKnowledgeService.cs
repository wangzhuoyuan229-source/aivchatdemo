using System.Text.Json;
using ChatApp.Core.Models;
using ChatApp.Core.Services;
using ChatApp.Core.Settings;
using ChatApp.Infrastructure.Data;
using Microsoft.Extensions.Logging;

namespace ChatApp.UI.Services;

public enum BundledKnowledgeImportStatus
{
    NotPackaged,
    Deferred,
    AlreadyCurrent,
    Imported,
    Partial
}

public sealed record BundledKnowledgeImportResult(
    BundledKnowledgeImportStatus Status,
    int Total = 0,
    int Imported = 0,
    int Skipped = 0,
    int Failed = 0,
    int MovedFromUngrouped = 0,
    int? GroupId = null,
    string Detail = "");

public sealed record BundledRoleAvatarCandidate(
    string SourcePath,
    string RelativePath,
    string Title);

/// <summary>
/// Imports the knowledge corpus shipped beside the executable into the user's
/// persistent vector store exactly once per bundle version. The source files are
/// immutable application content; generated vectors remain in AppData so an app
/// restart or an in-place upgrade does not repeat paid API work.
/// </summary>
public sealed class BundledKnowledgeService
{
    public const string GroupName = "内置知识库";
    internal const string BundleVersion = "2026-08-16-1";
    private const string BundleDirectoryName = "BundledKnowledge";
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        { "", "txt", "md", "markdown", "pdf", "png", "jpg", "jpeg", "webp" };

    private readonly IKnowledgeService _knowledge;
    private readonly IConfigurationService _configuration;
    private readonly ILogger<BundledKnowledgeService> _logger;
    private readonly string _bundleDirectory;
    private readonly string _markerPath;
    private readonly SemaphoreSlim _runGate = new(1, 1);

    public BundledKnowledgeService(
        IKnowledgeService knowledge,
        IConfigurationService configuration,
        ILogger<BundledKnowledgeService> logger)
        : this(
            knowledge,
            configuration,
            logger,
            ResolvePackagedBundleDirectory(),
            Path.Combine(AppPaths.AppDataDir, "bundled-knowledge.json"))
    {
    }

    internal BundledKnowledgeService(
        IKnowledgeService knowledge,
        IConfigurationService configuration,
        ILogger<BundledKnowledgeService> logger,
        string bundleDirectory,
        string markerPath)
    {
        _knowledge = knowledge;
        _configuration = configuration;
        _logger = logger;
        _bundleDirectory = Path.GetFullPath(bundleDirectory);
        _markerPath = Path.GetFullPath(markerPath);
    }

    private static string ResolvePackagedBundleDirectory()
    {
        var besideExecutable = Path.Combine(AppContext.BaseDirectory, BundleDirectoryName);
        if (Directory.Exists(besideExecutable)) return besideExecutable;

        // macOS app bundles keep non-code content under Contents/Resources so
        // codesign does not mistake extensionless knowledge files for binaries.
        return Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "Resources", BundleDirectoryName));
    }

    public bool IsPackaged => Directory.Exists(_bundleDirectory);

    public BundledRoleAvatarCandidate? FindRoleAvatarCandidate(string roleName)
    {
        try
        {
            return FindBestRoleAvatarFile(_bundleDirectory, roleName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to search bundled avatar folders for role {RoleName}.", roleName);
            return null;
        }
    }

    public async Task<BundledKnowledgeImportResult> EnsureImportedAsync(
        IProgress<KnowledgeImportProgress>? progress = null,
        CancellationToken ct = default)
    {
        await _runGate.WaitAsync(ct);
        try
        {
            if (!Directory.Exists(_bundleDirectory))
                return new(BundledKnowledgeImportStatus.NotPackaged, Detail: "安装包中未发现内置知识库目录。");

            var marker = await ReadMarkerAsync(ct);
            var layout = DiscoverBundle();
            if (layout.ExpectedRelativePaths.Count == 0)
                return new(BundledKnowledgeImportStatus.NotPackaged, Detail: "内置知识库中没有可索引的文件。");

            IReadOnlyList<KnowledgeGroup>? groups = null;
            if (marker?.BundleVersion == BundleVersion)
            {
                groups = await _knowledge.ListGroupsAsync(ct);
                var markedGroup = groups.FirstOrDefault(item =>
                    item.Id == marker.GroupId && string.Equals(item.Name, GroupName, StringComparison.Ordinal));
                if (markedGroup is not null)
                {
                    var documents = await _knowledge.ListDocumentsByGroupAsync(markedGroup.Id, ct);
                    var markedIndexedPaths = documents
                        .Where(document => document.ChunkCount > 0)
                        .Select(document => NormalizePath(document.SourceRelativePath))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    if (layout.ExpectedRelativePaths.All(markedIndexedPaths.Contains))
                    {
                        return new(
                            BundledKnowledgeImportStatus.AlreadyCurrent,
                            layout.ExpectedRelativePaths.Count,
                            Skipped: layout.ExpectedRelativePaths.Count,
                            GroupId: marker.GroupId,
                            Detail: "内置知识库已完成索引，无需重复导入。");
                    }
                }
            }

            groups ??= await _knowledge.ListGroupsAsync(ct);
            var group = groups.FirstOrDefault(item => string.Equals(item.Name, GroupName, StringComparison.Ordinal));
            if (group is null)
                group = await _knowledge.CreateGroupAsync(GroupName, ct);

            var settings = await _configuration.LoadAsync(ct);
            if (!CanGenerateEmbeddings(settings, out var deferredReason))
                return new(BundledKnowledgeImportStatus.Deferred, GroupId: group.Id, Detail: deferredReason);

            // A real bundle upgrade deliberately rebuilds only the reserved built-in
            // group. The first installation has no marker, so an interrupted/manual
            // import can instead be adopted without paying for the same vectors twice.
            if (marker is not null && marker.BundleVersion != BundleVersion)
            {
                var previousGroup = groups.FirstOrDefault(item => item.Id == marker.GroupId &&
                    string.Equals(item.Name, GroupName, StringComparison.Ordinal));
                if (previousGroup is not null)
                {
                    var oldDocuments = await _knowledge.ListDocumentsByGroupAsync(previousGroup.Id, ct);
                    if (oldDocuments.Count > 0)
                        await _knowledge.DeleteDocumentsAsync(oldDocuments.Select(item => item.Id).ToArray(), ct);
                }
            }

            var expected = layout.ExpectedRelativePaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var ungrouped = await _knowledge.ListDocumentsByGroupAsync(null, ct);
            var adoptIds = ungrouped
                .Where(document => document.ChunkCount > 0 && expected.Contains(NormalizePath(document.SourceRelativePath)))
                .Select(document => document.Id)
                .ToArray();
            if (adoptIds.Length > 0)
                await _knowledge.MoveDocumentsAsync(adoptIds, group.Id, ct);

            var capture = new ImportProgressCapture(progress);
            var imported = new List<KnowledgeDocument>();
            if (layout.RootDirectories.Count > 0)
            {
                imported.AddRange(await _knowledge.ImportDirectoriesAsync(
                    layout.RootDirectories,
                    recursive: true,
                    progress: capture,
                    ct: ct,
                    groupId: group.Id));
            }
            if (layout.RootFiles.Count > 0)
            {
                imported.AddRange(await _knowledge.ImportFilesAsync(
                    layout.RootFiles,
                    progress: capture,
                    ct: ct,
                    groupId: group.Id));
            }

            var indexed = await _knowledge.ListDocumentsByGroupAsync(group.Id, ct);
            var indexedPaths = indexed
                .Where(document => document.ChunkCount > 0)
                .Select(document => NormalizePath(document.SourceRelativePath))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missing = expected.Count(path => !indexedPaths.Contains(path));
            var reportedFailures = capture.Last?.Failed ?? 0;
            var failures = Math.Max(missing, reportedFailures);
            var skipped = Math.Max(0, layout.ExpectedRelativePaths.Count - imported.Count - failures);

            if (failures == 0)
            {
                await WriteMarkerAsync(new BundleMarker(BundleVersion, group.Id), ct);
                _logger.LogInformation(
                    "Built-in knowledge is current. Total={Total}, Imported={Imported}, Adopted={Adopted}, Group={GroupId}",
                    layout.ExpectedRelativePaths.Count,
                    imported.Count,
                    adoptIds.Length,
                    group.Id);
                return new(
                    BundledKnowledgeImportStatus.Imported,
                    layout.ExpectedRelativePaths.Count,
                    imported.Count,
                    skipped,
                    0,
                    adoptIds.Length,
                    group.Id,
                    "内置知识库已完成索引。");
            }

            _logger.LogWarning(
                "Built-in knowledge import is incomplete. Missing={Missing}, ReportedFailures={ReportedFailures}",
                missing,
                reportedFailures);
            return new(
                BundledKnowledgeImportStatus.Partial,
                layout.ExpectedRelativePaths.Count,
                imported.Count,
                skipped,
                failures,
                adoptIds.Length,
                group.Id,
                capture.Last?.LastError ?? "部分文件尚未完成索引，下次启动会自动续传。");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Automatic built-in knowledge import failed.");
            return new(BundledKnowledgeImportStatus.Partial, Failed: 1, Detail: ex.Message);
        }
        finally
        {
            _runGate.Release();
        }
    }

    private BundleLayout DiscoverBundle()
    {
        var directories = Directory.EnumerateDirectories(_bundleDirectory)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var rootFiles = Directory.EnumerateFiles(_bundleDirectory, "*", SearchOption.TopDirectoryOnly)
            .Where(IsSupported)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var relativePaths = new List<string>();
        relativePaths.AddRange(rootFiles.Select(path => Path.GetFileName(path)!));
        foreach (var directory in directories)
        {
            var rootName = new DirectoryInfo(directory).Name;
            relativePaths.AddRange(Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
                .Where(IsSupported)
                .Select(path => NormalizePath(Path.Combine(rootName, Path.GetRelativePath(directory, path)))));
        }
        return new BundleLayout(
            directories,
            rootFiles,
            relativePaths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    internal static BundledRoleAvatarCandidate? FindBestRoleAvatarFile(string bundleDirectory, string roleName)
    {
        if (!Directory.Exists(bundleDirectory)) return null;
        var normalizedName = NormalizeAvatarText(roleName);
        if (normalizedName.Length == 0) return null;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        return Directory.EnumerateFiles(bundleDirectory, "*", options)
            .Where(IsImageFile)
            .Select(path =>
            {
                var relativePath = NormalizePath(Path.GetRelativePath(bundleDirectory, path));
                var segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                var folders = segments.SkipLast(1).Select(NormalizeAvatarText).ToArray();
                var file = NormalizeAvatarText(Path.GetFileNameWithoutExtension(path));
                var folderScore = folders.Any(segment => segment == normalizedName)
                    ? 1000d
                    : folders.Any(segment => segment.Contains(normalizedName, StringComparison.OrdinalIgnoreCase))
                        ? 900d
                        : 0d;
                var fileScore = file.Contains(normalizedName, StringComparison.OrdinalIgnoreCase) ? 500d : 0d;
                var artworkScore = file == $"立绘{normalizedName}精二"
                    ? 100d
                    : file.Contains("精二", StringComparison.OrdinalIgnoreCase)
                        ? 30d
                        : file.Contains("精一", StringComparison.OrdinalIgnoreCase) ? 20d : 0d;
                if (file.Contains("残余", StringComparison.OrdinalIgnoreCase)) artworkScore -= 50;
                var depthPenalty = segments.Length;
                return new
                {
                    Candidate = new BundledRoleAvatarCandidate(
                        path,
                        relativePath,
                        Path.GetFileNameWithoutExtension(path)),
                    NameMatched = folderScore > 0 || fileScore > 0,
                    Score = folderScore + fileScore + artworkScore - depthPenalty
                };
            })
            .Where(item => item.NameMatched)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Candidate.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Candidate)
            .FirstOrDefault();
    }

    private static bool IsImageFile(string path) => Path.GetExtension(path).ToLowerInvariant() is
        ".png" or ".jpg" or ".jpeg" or ".webp";

    private static string NormalizeAvatarText(string value) => string.Concat(
        (value ?? string.Empty).Where(char.IsLetterOrDigit)).ToLowerInvariant();

    private static bool IsSupported(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path).TrimStart('.')) &&
        !string.Equals(Path.GetFileName(path), ".DS_Store", StringComparison.OrdinalIgnoreCase);

    private static bool CanGenerateEmbeddings(AiSettings settings, out string reason)
    {
        if (!settings.EnableKnowledgeBase)
        {
            reason = "启用知识库后，将自动索引内置资料。";
            return false;
        }
        if (string.IsNullOrWhiteSpace(settings.EmbeddingModel))
        {
            reason = "配置 Embedding 模型后，将自动索引内置资料。";
            return false;
        }
        var key = settings.ResolveEmbeddingApiKey();
        if (string.IsNullOrWhiteSpace(key))
        {
            reason = "配置 Embedding API Key 后，将自动索引内置资料。";
            return false;
        }
        var endpoint = settings.ResolveEmbeddingApiBaseUrl();
        if (!RemoteApiEndpointPolicy.TryNormalize(endpoint, out _, out var error))
        {
            reason = $"Embedding 地址无效（{error}），修正后将自动索引内置资料。";
            return false;
        }
        reason = string.Empty;
        return true;
    }

    private async Task<BundleMarker?> ReadMarkerAsync(CancellationToken ct)
    {
        if (!File.Exists(_markerPath)) return null;
        try
        {
            var json = await File.ReadAllTextAsync(_markerPath, ct);
            return JsonSerializer.Deserialize<BundleMarker>(json);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Ignoring unreadable built-in knowledge marker.");
            return null;
        }
    }

    private async Task WriteMarkerAsync(BundleMarker marker, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_markerPath)!);
        var temporaryPath = _markerPath + ".tmp";
        await File.WriteAllTextAsync(temporaryPath, JsonSerializer.Serialize(marker), ct);
        File.Move(temporaryPath, _markerPath, overwrite: true);
    }

    private static string NormalizePath(string path) =>
        (path ?? string.Empty).Replace('\\', '/').Trim('/');

    private sealed record BundleMarker(string BundleVersion, int GroupId);

    private sealed record BundleLayout(
        IReadOnlyList<string> RootDirectories,
        IReadOnlyList<string> RootFiles,
        IReadOnlyList<string> ExpectedRelativePaths);

    private sealed class ImportProgressCapture : IProgress<KnowledgeImportProgress>
    {
        private readonly IProgress<KnowledgeImportProgress>? _target;

        public ImportProgressCapture(IProgress<KnowledgeImportProgress>? target) => _target = target;

        public KnowledgeImportProgress? Last { get; private set; }

        public void Report(KnowledgeImportProgress value)
        {
            Last = value;
            _target?.Report(value);
        }
    }
}
