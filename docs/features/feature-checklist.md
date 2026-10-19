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

The original candidate `ca1a09752b262288422236db22375baa83db5b05` passed 12 hidden-browser tuples: 1440/320 widths, English/Cantonese/bilingual and light/dark, with reduced motion active and no horizontal body overflow. Native Tab, Space and Enter operated disclosure controls. Category/status intersection, empty results, Reset focus and preparation progress across reload passed. The inspected accessibility tree contained 217 named controls in the reviewed roles and no unnamed controls. Fifteen genuine captures were inspected. These are bounded observations, not complete accessibility or physical-device certification.

Review also found that optional source-reference metadata could render unescaped HTML. Source references now reject verification-only fields; binding output is limited to verification evidence and escaped. The saved inventory contained no injected content. The exact local build entrypoint passed 23 core, 51 UI-state and 13 checklist checks, including 123 independent omission cases and the observed failing-then-passing metadata regression. Independent review confirmed that current inventory rendering is unchanged by this repair.

The browser/server processes, ports and exact hidden desktop were verified absent. Automatic execution review rejected deletion of the isolated profile. It remains retained, the complete cleanup/promotion receipt is pending, and new captures are not published as accepted evidence. The milestone therefore remains implemented with verification pending. The checklist does not implement the native container health/device/GPU milestones or establish complete product acceptance. The public bounded summary is `evidence/checklist-verification.json`.

A later live check at `5d96573` verified all 41 internal anchors, 13 complete articles, three published PNGs and the exact source/manifest bytes. It found an implicit root favicon request returning 404. The documentation now declares a local `favicon.svg` matching the existing grid mark, with its hash in the content manifest. Fresh deployed resource verification is separate from the earlier checklist behavior evidence.

## 廣東話指南

文件首頁嘅功能完成清單由維護者管理，保留 109 項必需功能、10 項產品流程同四項里程碑。健康檢查、裝置對應同 GPU 控制仍然係計劃項目。已實作唔代表完整驗證，現時冇項目標示為完整驗證。

分類同狀態篩選會一齊生效；結果數目同空白提示會即時更新。重設篩選會顯示全部項目，並將鍵盤焦點放返分類。用 Tab 揀項目，再按 Enter 或空白鍵展開待辦事項同證據。語言選擇支援英文、廣東話同雙語，外觀跟隨淺色或深色設定，窄版面會直向排列。

呢份清單唔會俾訪客剔選完成。下面嘅準備清單仍然只儲存你自己嘅進度，原有儲存資料同重設功能保留。功能篩選只維持本次瀏覽，唔會寫入準備清單資料。

維護者修改 JSON 來源清單，再執行產生指令，同步首頁、功能摘要同內容雜湊。來源連結只係參考，完整驗證需要同一版本嘅檢查、實際操作同畫面證據。格式檢查唔能夠證明證據內容屬實，仍然要獨立檢查。

原候選版本已通過 12 組寬度、語言同主題組合，以及真實鍵盤展開、篩選、空白結果、重設焦點同準備進度保留。15 張畫面已檢查。來源參考嘅額外驗證資料曾有 HTML 轉義缺口，已修正並以先失敗後成功嘅測試確認。瀏覽器、伺服器、連接埠同指定隱藏桌面已確認結束；隔離設定目錄刪除被自動審核拒絕，所以完整清理同畫面正式採納仍未完成，唔會將有限驗證當成全產品驗收。
