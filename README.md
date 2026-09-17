# Part Comparison Tool

Tekla Structures 2023 utility for finding replacement steel assemblies in later phases.

## Workflow

1. Select any part belonging to the assembly that is missing on site.
2. Enter one or more Tekla phase numbers (for example `3,4,5`).
3. Click **Find Matches**.
4. The tool first narrows the model to assemblies whose main part has the same profile and length.
5. Candidates are then deeply compared using assembly weight, finish/material, secondary-part count and geometry, bolt groups/holes, and weld properties.
6. Results are ranked as **Exact**, **Very close**, **Close**, or **Possible** and show the differences that would need checked or modified.

The search deliberately does not discard a candidate just because its weight differs. Weight is used as a strong signal and ranking factor so assemblies missing a plate or other fitting can still be surfaced as useful near matches.

## Target

- Tekla Structures 2023
- Tekla.Structures.Model 2023.0.1
- .NET Framework 4.8
- C# 7.3
- WPF / x64

## Notes

This is an engineering comparison aid, not an automatic substitution approval. The result details are intended to make differences explicit before a piece is reassigned on site.
