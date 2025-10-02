# Use the official .NET 8.0 runtime image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80

# Use the .NET 8.0 SDK image for building the application
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["FinanceApi/FinanceApi.csproj", "FinanceApi/"]
RUN dotnet restore "FinanceApi/FinanceApi.csproj"
COPY . .
WORKDIR "/src/FinanceApi"
RUN dotnet build "FinanceApi.csproj" -c Release -o /app/build

# Publish the application
FROM build AS publish
RUN dotnet publish "FinanceApi.csproj" -c Release -o /app/publish

# Use .NET 8.0 runtime for the final image
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "FinanceApi.dll"]