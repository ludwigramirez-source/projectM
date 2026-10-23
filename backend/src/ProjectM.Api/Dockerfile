# Contexto de build: carpeta backend/
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ProjectM.sln ./
COPY src/ProjectM.Domain/ProjectM.Domain.csproj src/ProjectM.Domain/
COPY src/ProjectM.Api/ProjectM.Api.csproj src/ProjectM.Api/
RUN dotnet restore src/ProjectM.Api/ProjectM.Api.csproj
COPY src/ src/
RUN dotnet publish src/ProjectM.Api/ProjectM.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
RUN mkdir -p /data/attachments && chown -R app:app /data
COPY --from=build /app .
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "ProjectM.Api.dll"]
