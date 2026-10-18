# Material Docker Image

A native Windows workspace for building Docker images, running containers, inspecting resources, and understanding build stages on an interactive canvas.

Development is in progress. No release or runtime acceptance is claimed yet.

## Build

Run `build.bat`. The application requires Docker CLI and a reachable engine for Docker operations. Its editors and saved workspaces remain usable offline.

## Safety

Commands use structured arguments without a command shell. Existing Docker contexts and credential helpers remain owned by Docker. Destructive actions require explicit confirmation in the application.
