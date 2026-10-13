# Stage 1: Build & Publish
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project file and restore dependencies
COPY ["CrmLeadManagement/CrmLeadManagement.csproj", "CrmLeadManagement/"]
RUN dotnet restore "CrmLeadManagement/CrmLeadManagement.csproj"

# Copy all source files and publish release
COPY . .
WORKDIR "/src/CrmLeadManagement"
RUN dotnet publish "CrmLeadManagement.csproj" -c Release -o /app/publish --no-restore

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Copy compiled artifacts
COPY --from=build /app/publish .

# Environment variables
ENV ASPNETCORE_ENVIRONMENT=Production
ENV PORT=10000
EXPOSE 10000

# Copy entrypoint script to fix persistent volume permissions and run as $APP_UID
COPY entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh

ENTRYPOINT ["/entrypoint.sh"]
CMD ["dotnet", "CrmLeadManagement.dll"]
