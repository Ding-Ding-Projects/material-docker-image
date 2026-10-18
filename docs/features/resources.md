# Images, containers, storage and networks

Refresh inventories from the selected engine. Inspect an exact record before acting on it. Image tags can move and external tools can delete or replace resources, so refresh after every mutation.

Image workflows include pulling, inspecting, tagging, importing, exporting, running and removing images where the installed Docker capabilities support them. Container workflows include lifecycle actions, logs and resource inspection. Volumes and networks expose creation, inspection and removal through their dedicated workspace.

Removal is destructive and requires confirmation. A volume may contain irreplaceable data. Do not use broad prune commands as a substitute for selecting the exact records intended for deletion. The application never starts a container merely because its details are opened.

## 廣東話

先重新整理清單，再檢查要操作嗰個項目。標籤可以改指向，其他工具亦可以改動引擎，所以每次操作完都要再讀狀態。資料卷可能有重要資料，刪除前必須清楚確認，唔好用大範圍清理代替逐項選擇。
