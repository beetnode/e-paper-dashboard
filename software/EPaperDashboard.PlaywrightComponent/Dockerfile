# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS component

ARG TARGETARCH
ARG VERSION=0.0.0
ENV DEBIAN_FRONTEND=noninteractive
ENV PLAYWRIGHT_BROWSERS_PATH=/component/browsers

RUN apt-get update && apt-get install -y --no-install-recommends file && \
    rm -rf /var/lib/apt/lists/*

WORKDIR /src
COPY EPaperDashboard.PlaywrightComponent/ ./EPaperDashboard.PlaywrightComponent/
COPY RenderingComponentProtocol.cs ./RenderingComponentProtocol.cs
COPY EPaperDashboard.PlaywrightComponent/packaging/ ./packaging/

RUN case "$TARGETARCH" in \
      amd64) RID=linux-x64 ;; \
      arm64) RID=linux-arm64 ;; \
      *) echo "Unsupported architecture: $TARGETARCH" >&2; exit 1 ;; \
    esac && \
    dotnet publish EPaperDashboard.PlaywrightComponent/EPaperDashboard.PlaywrightComponent.csproj \
      -c Release -r "$RID" --self-contained false -o /component/worker \
      /p:Version="$VERSION" /p:DebugType=none /p:DebugSymbols=false

# Install the browser and its dependencies only in this packaging stage. The collector copies
# the required native libraries into the downloadable component; none enter the base image.
RUN pwsh /component/worker/playwright.ps1 install --with-deps --only-shell chromium
RUN bash /src/packaging/collect-native-libraries.sh /component
RUN case "$TARGETARCH" in \
      amd64) COMPONENT_RUNTIME=linux-x64 ;; \
      arm64) COMPONENT_RUNTIME=linux-arm64 ;; \
    esac && \
    sed \
      -e "s/__APP_VERSION__/$(echo "$VERSION" | cut -d. -f1-3)/g" \
      -e "s/__COMPONENT_VERSION__/$VERSION/g" \
      -e "s/__RUNTIME_IDENTIFIER__/$COMPONENT_RUNTIME/g" \
      /src/packaging/component-manifest.template.json > /component/component-manifest.json
RUN cp /src/packaging/NOTICE /component/NOTICE
COPY LICENSE /component/LICENSE
COPY THIRD-PARTY-NOTICES.txt /component/THIRD-PARTY-NOTICES.txt

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS package
COPY --from=component /component/ /component/

# Optional developer-only check. Artifact export does not execute a browser in CI.
FROM package AS verify
COPY EPaperDashboard.PlaywrightComponent/packaging/smoke-request.json /tmp/playwright-smoke-request.json
ENV PLAYWRIGHT_BROWSERS_PATH=/component/browsers \
    IZBOARD_PLAYWRIGHT_LIBRARY_PATH=/component/native/lib \
    IZBOARD_PLAYWRIGHT_DATA_PATH=/component/native/share \
    IZBOARD_PLAYWRIGHT_FONTCONFIG_PATH=/component/native/fontconfig
RUN dotnet /component/worker/EPaperDashboard.PlaywrightComponent.dll \
      < /tmp/playwright-smoke-request.json > /tmp/playwright-smoke-response.json && \
    test -s /tmp/playwright-component-smoke.png && \
    grep -q '"success":true' /tmp/playwright-smoke-response.json

FROM scratch AS export
COPY --from=component /component/ /
