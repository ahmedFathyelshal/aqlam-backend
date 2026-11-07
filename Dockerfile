# Aqlam Academy API. Build: docker build -t aqlam-api .
# Settings come from environment variables (DatabaseSettings__ConnectionString, JwtSettings__Rsa__..., see render.yaml).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Restore first so package downloads are cached between code changes.
COPY src/Core/AqlamAcademy.Domain/*.csproj src/Core/AqlamAcademy.Domain/
COPY src/Core/AqlamAcademy.Application/*.csproj src/Core/AqlamAcademy.Application/
COPY src/Infrastructure/AqlamAcademy.Localization/*.csproj src/Infrastructure/AqlamAcademy.Localization/
COPY src/Infrastructure/AqlamAcademy.Persistence/*.csproj src/Infrastructure/AqlamAcademy.Persistence/
COPY src/Infrastructure/AqlamAcademy.Migrators.PostgreSQL/*.csproj src/Infrastructure/AqlamAcademy.Migrators.PostgreSQL/
COPY src/Web/AqlamAcademy.Api/*.csproj src/Web/AqlamAcademy.Api/
RUN dotnet restore src/Web/AqlamAcademy.Api/AqlamAcademy.Api.csproj

COPY src/ src/
RUN dotnet publish src/Web/AqlamAcademy.Api/AqlamAcademy.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app .

# Render routes traffic to port 10000 and terminates HTTPS in front of the container.
ENV ASPNETCORE_HTTP_PORTS=10000 \
    ForwardedHeaders__Enabled=true
EXPOSE 10000
USER $APP_UID
ENTRYPOINT ["dotnet", "AqlamAcademy.Api.dll"]
