# Earlier screenshot sets

The current captures in `evidence/screenshots/` come from workflow run 42042876831 at source `c2c535cef9fbfe31cdb6521c3d4273c28c443589` (see `evidence/inventory.json`). Earlier sets were replaced in place and remain in the repository history with their own provenance:

| Set | Source | Workflow run | Where to find it |
| --- | --- | --- | --- |
| Later board views (81 captures, dark, light and 960 x 720) | `fcf1a247f29dfcb2633d364aea099fe08e1a03af` | 42017259445 | `git show 450e000:evidence/inventory.json` and `git checkout 450e000 -- evidence/screenshots` |
| Design parity pass (72 captures, dark, light and 960 x 720) | `e2195b2aca6f17440a7b5ed00081577e18643313` | 42007146276 | `git show b12f177:evidence/inventory.json` and `git checkout b12f177 -- evidence/screenshots` |
| Polish pass (72 captures, dark, light and 960 x 720) | `4a17021e24b96b60ee1ea8f128f9028ab4f83b08` | 41846028015 | `git show d07d756:evidence/inventory.json` and `git checkout d07d756 -- evidence/screenshots` |
| Advanced redesign (72 captures, dark, light and 960 x 720) | `40fa2560a0d05536d4b5aafdb8aee5acf6a99c3e` | 41718216967 | `git show 47aee49:evidence/inventory.json` and `git checkout 47aee49 -- evidence/screenshots` |
| Material 3 rewrite (54 captures) | `afc72c0df9be118fdb6d07c238a4ff3b458edc0d` | 41698541353 | the commit that introduced that inventory in `git log -- evidence/inventory.json` |

Earlier interface evidence (source `f370d66`, release v0.5.1) belongs to the previous interface and is not relabelled.
