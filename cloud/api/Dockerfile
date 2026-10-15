FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY Directory.Build.props ./
COPY cloud/devices/ cloud/devices/
COPY cloud/api/ cloud/api/
RUN dotnet publish cloud/api/FocusGuard.Cloud.Api.csproj -c Release -o /app --use-current-runtime --self-contained false
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/ ./
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "FocusGuard.Cloud.Api.dll"]
