FROM debian:bookworm-slim AS base
WORKDIR /workspace
COPY hello.txt /workspace/hello.txt

FROM base AS final
LABEL org.opencontainers.image.title="Material Docker Image example"
CMD ["/bin/cat", "/workspace/hello.txt"]
