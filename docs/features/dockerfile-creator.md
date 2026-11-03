# Dockerfile Creator

The Creator builds a Dockerfile from structured steps, shows the exact text as you work, and checks it against common best practices.

## Start

Choose a starter: Node.js service, Python with pip, Python with uv, .NET (ASP.NET), Go static binary, Java (Maven build with a JRE runtime), Rust, static site served by an unprivileged nginx, or Blank. Each starter states what it creates. Applying a starter replaces the current draft; the previous draft is recorded in local history first so it can be restored from Activity. You can also import an existing Dockerfile: simple instructions become structured steps, and anything the editor cannot represent (heredocs, unknown flags or instructions) is kept as raw lines and reported.

## Stages

The left column lists the stages. Add, rename, duplicate, delete (with confirmation), reorder, or set a stage as the build target. Each stage has a base image (editable, with common images suggested), a name and an optional platform.

## Steps

Each instruction is a card with its keyword, a summary and a hint. Expand a card to edit it with a form specific to its kind:

- `ARG`, `ENV` and `LABEL` as name and value rows.
- `COPY` and `ADD` with sources, destination, `--from` stage, `--chown`, `--chmod` and `--link`.
- `RUN` as shell or exec form, with an optional cache mount target and secret mount id.
- `USER`, `WORKDIR`, `EXPOSE` (port and protocol), `VOLUME`, `STOPSIGNAL`, `SHELL`, `ONBUILD` and comments.
- `ENTRYPOINT` and `CMD` in exec form with one field per argument.
- `HEALTHCHECK` with command, interval, timeout, retries and start period.

Fields are validated as you type. Move steps with the arrow buttons or Alt+Up and Alt+Down; duplicate or delete them from the card or its context menu. Add instruction opens a searchable list, and quick-add chips insert a cached `RUN`, a `HEALTHCHECK`, a non-root `USER` or an `EXPOSE`.

## Preview and checks

The live preview shows the exact Dockerfile, rendered deterministically with correct exec-form escaping and an optional `# syntax` header. It has its own search and a copy button.

The Checks panel reports, among others: an unpinned base image, a final stage that runs as root, no health check, `apt-get update` without an install in the same step, missing `--no-install-recommends` or cleanup, `ADD` used for local files, shell-form `CMD` or `ENTRYPOINT`, a web service without `EXPOSE`, `COPY . .` before dependency installation, secret-looking `ARG` or `ENV` names, duplicate stage names, `COPY --from` pointing to an unknown or later stage, a relative `WORKDIR`, and several `CMD` instructions. Most findings have a Fix button; fixes that need a value (such as a tag or digest to pin) ask for it first.

## Saving and building

Undo and Redo keep the last 200 changes. The draft is saved automatically in the application data folder and recorded in local history after two seconds without changes. Save as writes a Dockerfile you choose. Open in Build sends the text to the Build destination as an unsaved draft together with the selected target stage, so you can review it and build.
