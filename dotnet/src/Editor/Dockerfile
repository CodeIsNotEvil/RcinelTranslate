# The editor as a container image. Build context: the dotnet/ folder, so Directory.Build.props (the
# CINE namespace and the version) is part of the build. Works unchanged with podman and docker;
# see the editor section of the README for running it.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Build.targets ./
COPY src/Translate/Rcinel.Translate.csproj src/Translate/
COPY src/Core/Rcinel.Translate.Core.csproj src/Core/
COPY src/Ai/Rcinel.Translate.Ai.csproj src/Ai/
COPY src/Editor/Rcinel.Translate.Editor.csproj src/Editor/
# Restore on its own layer: the packages change far less often than the code.
RUN dotnet restore src/Editor/Rcinel.Translate.Editor.csproj
COPY src/ src/
# Without --no-restore: newer SDKs decide at restore time, from the .razor files, whether to add
# Blazor's framework scripts (_framework/blazor.web.js). The first restore only saw the project
# files, and an image built from it serves pages that never become interactive. The second restore
# finds the packages already in the cache and only adds what's missing.
RUN dotnet publish src/Editor/Rcinel.Translate.Editor.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# /data holds users and sign-in keys. World-writable with the sticky bit, because the container may
# run as root (rootless Podman, where root is you) or as your own uid (Docker); a named volume
# takes these permissions from the image when it's created.
RUN mkdir -p /data /project && chmod 1777 /data
ENV ASPNETCORE_HTTP_PORTS=8080 \
    Rcinel__Project=/project \
    Rcinel__Data=/data
EXPOSE 8080
ENTRYPOINT ["dotnet", "Rcinel.Translate.Editor.dll"]
