# Feature completion checklist

The documentation home page starts with a maintainer-owned completion checklist. It retains all 109 required capability identifiers, the 10 existing product workflows, and four explicit milestones: the checklist itself, container health checks, device mappings and GPU access. These last three container controls remain planned work.

## Read the status honestly

- Unverified: complete implementation and acceptance have not been established.
- Planned: the named milestone still needs implementation and verification.
- Implemented, verification pending: bounded implementation exists, but the remaining actions are still open.
- Fully verified: requires source, passing checks, built interaction and capture references tied to one exact source commit and artifact hash. The current inventory has no fully verified rows.

Source references explain the implementation or its recorded limits. They are explicitly not full verification. Existing historical workflow evidence keeps its original source and scope. All 109 required capability rows remain unverified.

## Browse the checklist

Choose a category and a status to intersect both filters. The live result count and empty-result message explain the current view. Reset feature filters restores all rows and returns keyboard focus to Category. Each row uses a native details/summary disclosure: Tab reaches it, and Enter or Space opens its remaining actions and evidence links. Filtering does not alter maintainer status or the documentation search.

English, Cantonese and bilingual labels follow the existing language selector. The checklist inherits light/dark appearance and reduced-motion behavior. At narrow widths its controls and status labels stack. No completion checkbox is offered to visitors.

The separate preparation checklist remains browser-owned. Its existing `mdi-guide-v1` storage, four checkbox IDs and reset behavior are preserved. Feature filters are visit-only state, do not access storage and cannot overwrite preparation progress.

## Maintain one inventory

Edit `website/feature-inventory.json` to update names, category, status, remaining actions and public source/evidence references. The required baseline in `website/required-feature-ids.json` protects the original 109 identifiers; the validator separately names every product workflow and milestone. It is not a second status inventory.

Run `node website/render.mjs` to regenerate the home page, the required-feature section of the capability inventory and deterministic content hashes. The renderer embeds all feature articles, project reference documents and available wiki Markdown, including this guide. It preserves content-addressed evidence PNGs and validates their exact bytes. `node website/render.mjs --check` rejects stale generated HTML, summary, manifest or images without writing output. The manifest records source hashes, not a fabricated release timestamp.

The local `build.bat -Tests` route runs checklist checks after the existing core and UI-state checks. Negative cases remove every entry independently, reject duplicate IDs, invalid status, absent language/evidence, unsupported or credential-bearing URLs, unsubstantiated completion and mismatched evidence bindings. A controller check exercises the actual filter script with bounded DOM substitutes. This is logic evidence, not real-browser or visual acceptance.

Only HTTPS references within this public project's repository or documentation home are accepted, with no credentials, query strings, encoded paths or arbitrary hosts. Verification evidence must use immutable repository evidence paths and record its UTC time, source commit, artifact hash, passing result and scope. These structural checks cannot prove that a receipt is true; maintainers must inspect genuine evidence before promoting a row.

## Verification limits

The checklist implementation requires independent hidden-browser interaction, keyboard, language/theme, narrow-layout and deployed-content verification. Until those receipts exist, the feature-checklist milestone remains implemented with verification pending. The checklist does not implement the native container health/device/GPU milestones or establish complete product acceptance.

## 廣東話指南

文件首頁嘅功能完成清單由維護者管理，保留 109 項必需功能、10 項產品流程同四項里程碑。健康檢查、裝置對應同 GPU 控制仍然係計劃項目。已實作唔代表完整驗證，現時冇項目標示為完整驗證。

分類同狀態篩選會一齊生效；結果數目同空白提示會即時更新。重設篩選會顯示全部項目，並將鍵盤焦點放返分類。用 Tab 揀項目，再按 Enter 或空白鍵展開待辦事項同證據。語言選擇支援英文、廣東話同雙語，外觀跟隨淺色或深色設定，窄版面會直向排列。

呢份清單唔會俾訪客剔選完成。下面嘅準備清單仍然只儲存你自己嘅進度，原有儲存資料同重設功能保留。功能篩選只維持本次瀏覽，唔會寫入準備清單資料。

維護者修改 JSON 來源清單，再執行產生指令，同步首頁、功能摘要同內容雜湊。來源連結只係參考，完整驗證需要同一版本嘅檢查、實際操作同畫面證據。格式檢查唔能夠證明證據內容屬實，仍然要獨立檢查。真實瀏覽器、鍵盤、所有語言與主題、窄版面同部署驗證尚待完成。
