# Use the official .NET image for running the application
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 5096

# Use the .NET SDK image for building the application
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["FinanceApi/FinanceApi.csproj", "FinanceApi/"]
RUN dotnet restore "FinanceApi/FinanceApi.csproj"
COPY . .
WORKDIR "/src/FinanceApi"
RUN dotnet build "FinanceApi.csproj" -c Release -o /app/build

# Publish the application
FROM build AS publish
RUN dotnet publish "FinanceApi.csproj" -c Release -o /app/publish

# Runtime stage with a smaller image
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "FinanceApi.dll"]
