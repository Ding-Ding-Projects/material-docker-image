# Build and run

## Build a local project

Choose a context folder and Dockerfile in the Build workspace. Review the document before running it: Dockerfile instructions can execute commands in the selected build environment. Choose an image tag, optional target stage, platform, build arguments, and cache behavior. The selected Docker context applies to each command without changing the user's global selection.

Start the build and inspect the streamed output. A successful exit reports command completion; inspect the resulting image before treating a mutable tag as a particular immutable result. Cancellation terminates the local CLI process, and engine-side state must be refreshed before retrying.

## Run an image

Choose an image, configure the container name, ports, environment, mounts, network, restart behavior, and resource limits exposed by the Run editor. Review writable host mounts carefully. Start the container, then inspect its state and logs. A created container is not necessarily a healthy service.

## Compose

Open a Compose file, review or edit it, and validate the resolved configuration before starting services. Down removes the project's service containers. Persistent volume deletion must be a separate explicit choice.

## Troubleshooting

An unavailable engine, missing build context, invalid tag, unsupported platform, or failing build step produces a real unsuccessful result. Correct the reported cause, refresh engine state, and retry. Keep protected values out of build arguments and use Docker-supported secret routes when available.

## 廣東話

先揀內容資料夾同 Dockerfile，再檢查標籤、目標階段、平台同快取設定。建置時睇實輸出，成功退出唔等於服務健康。停止本機指令之後，要重新整理引擎狀態先再試。Compose 啟動前先驗證設定，刪除持久資料必須另外明確選擇。
