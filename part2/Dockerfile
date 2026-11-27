FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/ ./src/
RUN dotnet restore src/Subscriptions.Api/Subscriptions.Api.csproj
RUN dotnet publish src/Subscriptions.Api/Subscriptions.Api.csproj -c Release --no-restore -o /out
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
ENV ASPNETCORE_HTTP_PORTS=8080 Database__Path=/data/subscriptions.db
EXPOSE 8080
# Mount /data with write access for the chosen runtime user. Production also requires Jwt__Key.
ENTRYPOINT ["dotnet", "Subscriptions.Api.dll"]
