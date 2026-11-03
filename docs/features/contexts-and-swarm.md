# Engine contexts and Swarm

## Contexts

Contexts shows every Docker engine context as a card with its description, endpoint, transport, whether it is the CLI's current context and whether this window uses it. Every context is probed in parallel (ten-second limit) and shows its engine version, platform and API version, or the error.

- **Use in this window** switches only this window and records the change in local history. The app never runs `docker context use`.
- **Create** offers endpoint presets for named pipe, Unix socket, TCP and SSH, TLS certificate pickers and a live command preview.
- **Update**, **Inspect** (sanitized), **Export** (with a warning about key material) and **Import** are available. **Remove** asks first and refuses the default context and the one this window uses.
- **Plugins** lists the CLI plugins, failed plugins and CLI warnings, with search. A note shows where the CLI configuration lives and warns when `DOCKER_HOST` or `DOCKER_CONTEXT` is set.

## Swarm

Swarm reads the engine's swarm state and shows what fits it.

- **Not in a swarm**: Init (optional advertise address) and Join (token in a password field) with confirmation.
- **Workers, pending or locked nodes**: an explanation and Leave. Leave asks first; forced leave needs the super confirmation.
- **Managers**: a statistics strip and tabs:
  - **Services**: replica chips, a task timeline from `docker service ps`, logs with follow, scale, update image, rollback, remove and a create form with ports, environment and constraints. Environment values are masked in the preview and never stored.
  - **Stacks**: deploy from a Compose file, list services and remove with a super confirmation listing them.
  - **Nodes**: availability (active, pause, drain), promote, demote and inspect.
  - **Secrets**: create from a typed value sent on standard input or from a file the CLI reads; inspect shows metadata only.
  - **Configs**: create from a file, view decoded content, inspect and remove.

## Volume backup, restore and clone

In Storage, a volume can be backed up to a `.tar.gz` archive with a short-lived helper container that has no network. Containers using the volume can be stopped for the backup and are always started again afterwards. Restore writes an archive into an existing or new volume; replacing current contents needs the super confirmation. Clone copies a volume into a new one without using a host folder, so it also works with remote engines.

## Compose tools

Compose projects gain watch (only when a service declares `develop.watch`; otherwise a worked example explains how), top, images, a live events list, copy files in both directions, one-shot exec, extra environment files and a dry-run switch for Up and Down.

