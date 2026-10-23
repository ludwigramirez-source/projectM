# Imagen para correr las pruebas dentro de la red de Docker Compose (perfil "test").
FROM mcr.microsoft.com/dotnet/sdk:10.0
WORKDIR /src
COPY . .
RUN dotnet restore
ENTRYPOINT ["dotnet", "test", "--no-restore", "--logger", "console;verbosity=normal"]
