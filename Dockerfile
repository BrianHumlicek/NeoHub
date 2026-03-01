# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project files
COPY ["NeoHub/NeoHub/NeoHub.csproj", "NeoHub/NeoHub/"]
COPY ["NeoHub/TLink/DSC.TLink.csproj", "NeoHub/TLink/"]

# Restore dependencies
RUN dotnet restore "NeoHub/NeoHub/NeoHub.csproj"

# Copy all source code
COPY . .

# GitHub Actions metadata passed in at docker build time
ARG GITHUB_SHA
ARG GITHUB_RUN_NUMBER

# Build and publish
WORKDIR "/src/NeoHub/NeoHub"
RUN dotnet publish "NeoHub.csproj" -c Release -o /app/publish /p:UseAppHost=false /p:GITHUB_SHA=$GITHUB_SHA /p:GITHUB_RUN_NUMBER=$GITHUB_RUN_NUMBER

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Expose ports
EXPOSE 8080
EXPOSE 8443
EXPOSE 3072

# Copy published app
COPY --from=build /app/publish .

# Create persist directory and declare as volume
RUN mkdir -p /app/persist
VOLUME /app/persist

ENTRYPOINT ["dotnet", "NeoHub.dll"]