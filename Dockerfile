
# ===============================
# 🔧 Etapa 1: Build
# ===============================
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

# Copiar archivos de proyecto
COPY *.sln .
COPY */*.csproj ./

# Restaurar dependencias
RUN dotnet restore

# Copiar todo el código
COPY . .

# Publicar la aplicación
RUN dotnet publish -c Release -o /publish

# ===============================
# 🚀 Etapa 2: Runtime
# ===============================
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Copiar archivos publicados desde build
COPY --from=build /publish .

# Puerto de la app
EXPOSE 80

# Ejecutar la app
ENTRYPOINT ["dotnet", "ActivaFest.dll"]
