# Grounding evaluation set

`grounding-cases.json` contains 50 fixed cases covering known facts, paraphrases,
follow-up references, unknown facts, prompt injection, casual conversation,
cross-role isolation, and conflicting sources.

For a model-backed evaluation, run every case three times against the same model
snapshot and settings. Record:

- grounded-fact correctness;
- correct in-character uncertainty for unsupported facts;
- cross-role leakage;
- human naturalness score from 1 to 5;
- repetitive opening, forced summary, and unnecessary closing-question rate.

Pass thresholds are 95% grounded correctness, 95% correct uncertainty, zero
cross-role leakage, average naturalness of at least 4/5, and repetitive-pattern
rate below 10%. Automated unit tests validate dataset shape and deterministic
retrieval/prompt invariants without making paid API calls.
