# The Linux backend's tests, in real windows under Xvfb:
#   docker build -t oriel-linux tests/linux
#   docker run --rm -v "$PWD:/repo:ro" oriel-linux
# The repository is copied into the container, so that its build output
# stays apart from the host's.
FROM mcr.microsoft.com/dotnet/sdk:10.0
RUN apt-get update \
 && apt-get install -y --no-install-recommends libgtk-3-0 libwebkit2gtk-4.1-0 xvfb dbus-x11 \
 && rm -rf /var/lib/apt/lists/*
ENV ORIEL_E2E=1 DOTNET_NOLOGO=1 DOTNET_CLI_TELEMETRY_OPTOUT=1
COPY run.sh /run.sh
CMD ["bash", "/run.sh"]
