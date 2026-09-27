# Build: restore first (cached until a project file changes), then publish.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["Directory.Build.props", "Directory.Packages.props", "./"]
COPY ["FinanceApi/FinanceApi.csproj", "FinanceApi/"]
COPY ["Finance.Domain/Finance.Domain.csproj", "Finance.Domain/"]
COPY ["Finance.Contracts/Finance.Contracts.csproj", "Finance.Contracts/"]
COPY ["Finance.Application/Finance.Application.csproj", "Finance.Application/"]
COPY ["Finance.Infrastructure/Finance.Infrastructure.csproj", "Finance.Infrastructure/"]
RUN dotnet restore "FinanceApi/FinanceApi.csproj"

COPY . .
RUN dotnet publish "FinanceApi/FinanceApi.csproj" -c Release -o /app/publish --no-restore

# Runtime: the .NET 8 image listens on 8080 and provides a non-root user.
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "FinanceApi.dll"]
