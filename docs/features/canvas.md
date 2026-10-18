# Canvas

The Canvas workspace provides a visual view of Dockerfile stages and runtime container relationships. Pan and zoom to inspect larger graphs, select a node to see its details, and return to the source editor for syntax that the current parser cannot safely round-trip.

Stage edges represent parsed dependencies such as `FROM` and `COPY --from`. A dependency graph is an explanation of configuration, not proof that a build has run. Runtime nodes come from a refreshed engine inventory and may become stale when an external tool changes containers or networks.

The editor must preserve unsupported instructions. A partial parser must not silently rewrite a complete Dockerfile or invent a dependency for an unresolved expression. Save only intentional changes and review the resulting text.

## Verification status

Implementation and built interaction evidence are tracked separately in the capability inventory. Graph rendering alone does not prove drag, zoom, editing, or synchronization behavior.

## 廣東話

畫布顯示建置階段同容器關係。可以移動畫面、縮放同揀節點睇詳情。連線表示設定入面嘅關係，唔代表建置已經執行。未能安全轉換嘅 Dockerfile 語法要保留，唔可以因為畫面靚就偷偷改走。
