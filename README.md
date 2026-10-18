# Material Docker Image

A native Windows workspace for building Docker images, running containers, inspecting resources, and understanding build stages on an interactive canvas.

Development is in progress. The core engine and canvas-state checks pass; complete capability acceptance remains open.

## Workflows

- Build local Dockerfiles with target stages, tags, platforms, build arguments and cache choices.
- Pull, inspect, run, tag, import, export and remove images.
- Start, stop, restart, inspect and view container logs and statistics.
- Edit and operate Compose projects with save-path protection.
- Inspect and manage volumes and networks on an explicitly selected engine.
- Explore Dockerfile stages and real container relationships on a selectable, pannable, zoomable canvas.

[Documentation](https://ding-ding-projects.github.io/material-docker-image/) · [Downloads](https://github.com/Ding-Ding-Projects/material-docker-image/releases) · [Capability inventory](docs/features/capability-inventory.md) · [Progress](https://github.com/Ding-Ding-Projects/material-docker-image/discussions/2)

## Build

Run `build.bat`. The application requires Docker CLI and a reachable engine for Docker operations. Its editors and saved workspaces remain usable offline.

Run `build.bat -Tests` for focused engine and canvas/state checks, `build.bat -DockerSmoke` for an explicitly mutating, uniquely owned local-engine smoke, and `build-installer.bat` for the genuine unsigned Squirrel.Windows package. The smoke currently targets the local `desktop-linux` context and uses the cached `debian:bookworm-slim` base image.

Native hidden-desktop verification can use `MaterialDockerImage.exe --software-rendering`. This selects WPF's supported software-rendering route; it changes rendering, not Docker behavior.

## Safety

Commands use structured arguments without a command shell. Existing Docker contexts and credential helpers remain owned by Docker. Destructive actions require explicit confirmation in the application.
