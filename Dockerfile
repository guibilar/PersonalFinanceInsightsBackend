# syntax=docker/dockerfile:1
# Build: restore first (cached until a project file changes), then publish.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Directory.Build.props", "Directory.Packages.props", "NuGet.Config", "./"]
COPY ["FinanceApi/FinanceApi.csproj", "FinanceApi/"]
COPY ["Finance.Domain/Finance.Domain.csproj", "Finance.Domain/"]
COPY ["Finance.Contracts/Finance.Contracts.csproj", "Finance.Contracts/"]
COPY ["Finance.Application/Finance.Application.csproj", "Finance.Application/"]
COPY ["Finance.Infrastructure/Finance.Infrastructure.csproj", "Finance.Infrastructure/"]
# Hestia.Contracts comes from GitHub Packages (NuGet.Config): the token is a build secret, so it is not in any image layer.
RUN --mount=type=secret,id=hestia_packages_user,env=HESTIA_PACKAGES_USER \
    --mount=type=secret,id=hestia_packages_token,env=HESTIA_PACKAGES_TOKEN \
    dotnet restore "FinanceApi/FinanceApi.csproj"

COPY . .
RUN dotnet publish "FinanceApi/FinanceApi.csproj" -c Release -o /app/publish --no-restore

# Runtime: the .NET image listens on 8080 and provides a non-root user.
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "FinanceApi.dll"]
