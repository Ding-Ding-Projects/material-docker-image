namespace MaterialDockerImage.UI;

/// <summary>
/// A service image that Docker Compose builds from a source repository instead of pulling. <see cref="Context"/> is a
/// Git URL pinned to one commit; <see cref="Dockerfile"/> is written into the Compose file as dockerfile_inline.
/// </summary>
public sealed record SelfHostedBuild(string Context, string Dockerfile);

/// <summary>Source builds used by catalogue entries. Each is pinned to one commit that was built and checked.</summary>
public static class SelfHostedSourceBuilds
{
    public const string HomeAssistantMaterialRepository = "https://github.com/Ding-Ding-Projects/home-assistant-material.git";
    /// <summary>The source of release 2026.11.1.132.</summary>
    public const string HomeAssistantMaterialCommit = "311d3508ad61da723de1ab7a561de1c50f0c8ceb";
    public const string HomeAssistantMaterialCore = "2026.11.1";
    public const string HomeAssistantMaterialBase = "2026.10.3";
    public static string HomeAssistantMaterialImage => $"material-docker-image/home-assistant-material:{HomeAssistantMaterialCore}-source.{HomeAssistantMaterialCommit[..12]}";

    /// <summary>
    /// Home Assistant Material from its repository: the Material frontend wheel (the repository's build/frontend.Dockerfile),
    /// the integration translations of the matching upstream release (build/translations.Dockerfile), then the repository's
    /// core/Dockerfile reading from the assembled tree. Nothing of Home Assistant Material is downloaded prebuilt.
    /// </summary>
    public static SelfHostedBuild HomeAssistantMaterial { get; } = new(HomeAssistantMaterialRepository + "#" + HomeAssistantMaterialCommit, $$"""
        # Home Assistant Material, built from its source repository at {{HomeAssistantMaterialCommit}}.
        # Stage 1 builds the Material frontend wheel (the repository's build/frontend.Dockerfile).
        FROM node:24.22.1-bookworm AS frontend
        RUN apt-get update && apt-get install -y --no-install-recommends python3 python3-venv git && rm -rf /var/lib/apt/lists/* && corepack enable
        WORKDIR /src
        COPY frontend/ .
        ENV HUSKY=0 CI=1 NODE_OPTIONS=--max_old_space_size=6144 SKIP_FETCH_NIGHTLY_TRANSLATIONS=1 HA_BUILD_WORKERS=4
        RUN pnpm install --frozen-lockfile
        RUN ./node_modules/.bin/gulp build-app || HA_BUILD_WORKERS=2 ./node_modules/.bin/gulp build-app
        RUN python3 -m venv /venv && /venv/bin/pip install --no-cache-dir build && rm -rf dist home_assistant_frontend.egg-info && /venv/bin/python -m build -q --wheel
        # Stage 2 takes only the integration translation files of the matching upstream release (build/translations.Dockerfile).
        FROM ghcr.io/home-assistant/amd64-homeassistant:{{HomeAssistantMaterialCore}} AS translations
        RUN mkdir /out && cd /usr/src/homeassistant/homeassistant && find components -path '*/translations/*.json' | tar cf - -T - | tar xf - -C /out
        # Stage 3 assembles the core source tree with the wheel and the translations.
        FROM scratch AS source
        COPY core/ /
        COPY --from=frontend /src/dist/ /
        COPY --from=translations /out/ /homeassistant/
        # Stage 4 is the repository's core/Dockerfile, reading from the assembled tree.
        FROM ghcr.io/home-assistant/amd64-homeassistant-base:{{HomeAssistantMaterialBase}}
        LABEL io.hass.type="core" io.hass.arch="amd64" io.hass.version="{{HomeAssistantMaterialCore}}" org.opencontainers.image.title="Home Assistant Material" org.opencontainers.image.source="https://github.com/Ding-Ding-Projects/home-assistant-material" org.opencontainers.image.revision="{{HomeAssistantMaterialCommit}}" org.opencontainers.image.licenses="Apache-2.0"
        ENV S6_SERVICES_GRACETIME=240000 UV_SYSTEM_PYTHON=true UV_NO_CACHE=true
        WORKDIR /usr/src
        COPY --from=source /rootfs/ /
        RUN echo "source;{{HomeAssistantMaterialCommit}};material-docker-image;local" > /OFFICIAL_IMAGE
        COPY --from=ghcr.io/alexxit/go2rtc:1.9.15@sha256:e098de5c2f8040dc3d4784ed2d5cfdac3b0979f9349afe587ef6a670a14b9f4d /usr/local/bin/go2rtc /bin/go2rtc
        COPY --from=source /requirements.txt homeassistant/
        COPY --from=source /homeassistant/package_constraints.txt homeassistant/homeassistant/
        RUN go2rtc --version && pip3 install --no-cache-dir "uv==$(awk -F'==' '/^uv==/{print $2}' homeassistant/requirements.txt)" && uv pip install --no-build -r homeassistant/requirements.txt
        COPY --from=source /requirements_all.txt /home_assistant_frontend-*.whl homeassistant/
        RUN uv pip install homeassistant/home_assistant_frontend-*.whl && uv pip install --no-build -r homeassistant/requirements_all.txt
        COPY --from=source /LICENSE.md /README.rst /pyproject.toml homeassistant/
        COPY --from=source /homeassistant/ homeassistant/homeassistant/
        RUN uv pip install -e ./homeassistant && python3 -m compileall -q homeassistant/homeassistant && python3 -c "import importlib.metadata as m; print('frontend', m.version('home-assistant-frontend'))"
        WORKDIR /config

        """);
}
