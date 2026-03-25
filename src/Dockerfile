FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Loggles.Core/Loggles.Core.csproj Loggles.Core/
COPY Loggles.Infrastructure/Loggles.Infrastructure.csproj Loggles.Infrastructure/
COPY Loggles.Api/Loggles.Api.csproj Loggles.Api/

RUN dotnet restore Loggles.Api/Loggles.Api.csproj

COPY Loggles.Core/ Loggles.Core/
COPY Loggles.Infrastructure/ Loggles.Infrastructure/
COPY Loggles.Api/ Loggles.Api/

RUN dotnet publish Loggles.Api/Loggles.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

COPY --from=build /app/publish .

VOLUME ["/data"]
ENV Storage__Provider="sqlite"
ENV Storage__ConnectionString="Data Source=/data/logs.db"
ENV ASPNETCORE_URLS="http://+:5000"

EXPOSE 5000

ENTRYPOINT ["dotnet", "Loggles.Api.dll"]
