# Use the official .NET 8 SDK image for building the application
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj and restore as distinct layers
COPY ["StoicTrade.Api.csproj", "./"]
RUN dotnet restore "StoicTrade.Api.csproj"

# Copy the rest of the application code
COPY . .

# Build the application
RUN dotnet build "StoicTrade.Api.csproj" -c Release -o /app/build

# Publish the application
FROM build AS publish
RUN dotnet publish "StoicTrade.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Use the official .NET 8 ASP.NET runtime image for running the application
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Ensure we have timezone data available for standard .NET time features
RUN apt-get update && apt-get install -y tzdata && rm -rf /var/lib/apt/lists/*
ENV TZ=Asia/Kolkata

EXPOSE 8080

# Copy the published output from the build stage
COPY --from=publish /app/publish .

# Set default environment variables
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["dotnet", "StoicTrade.Api.dll"]
