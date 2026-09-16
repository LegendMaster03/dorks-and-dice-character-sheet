FROM node:24-alpine AS frontend
WORKDIR /src/src/CharacterSheet.Web/Client

COPY ["src/CharacterSheet.Web/Client/package.json", "./"]
COPY ["src/CharacterSheet.Web/Client/tsconfig.json", "./"]
COPY ["src/CharacterSheet.Web/Client/vite.config.ts", "./"]
COPY ["src/CharacterSheet.Web/Client/src", "./src"]
COPY ["src/CharacterSheet.Web/Client/test", "./test"]
RUN npm install --no-audit --no-fund && npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Directory.Build.props", "./"]
COPY ["src/CharacterSheet.Domain/CharacterSheet.Domain.csproj", "src/CharacterSheet.Domain/"]
COPY ["src/CharacterSheet.Application/CharacterSheet.Application.csproj", "src/CharacterSheet.Application/"]
COPY ["src/CharacterSheet.Infrastructure/CharacterSheet.Infrastructure.csproj", "src/CharacterSheet.Infrastructure/"]
COPY ["src/CharacterSheet.Web/CharacterSheet.Web.csproj", "src/CharacterSheet.Web/"]
RUN dotnet restore "src/CharacterSheet.Web/CharacterSheet.Web.csproj"

COPY . .
COPY --from=frontend /src/src/CharacterSheet.Web/wwwroot ./src/CharacterSheet.Web/wwwroot
RUN dotnet publish "src/CharacterSheet.Web/CharacterSheet.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false -p:BuildClient=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "CharacterSheet.Web.dll"]
