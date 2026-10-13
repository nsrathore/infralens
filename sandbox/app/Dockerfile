# Build from the repo root:
#   docker buildx build --platform linux/amd64 -f sandbox/app/Dockerfile -t infralens-sandbox .
# The SDK stage runs natively (e.g. arm64 on a Mac) and cross-compiles for the target platform.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY sandbox/app/Sandbox.Api.csproj sandbox/app/
RUN dotnet restore sandbox/app/Sandbox.Api.csproj -a $TARGETARCH
COPY sandbox/app/ sandbox/app/
RUN dotnet publish sandbox/app/Sandbox.Api.csproj -c Release -a $TARGETARCH --no-restore -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
ENV PORT=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "sandbox-api.dll"]
