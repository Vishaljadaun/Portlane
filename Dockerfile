FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY server/CoreWaypoint.csproj server/
RUN dotnet restore server/CoreWaypoint.csproj
COPY server/ server/
COPY web/ web/
RUN dotnet publish server/CoreWaypoint.csproj -c Release --no-restore -o /app
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
ENTRYPOINT ["dotnet","CoreWaypoint.dll"]
