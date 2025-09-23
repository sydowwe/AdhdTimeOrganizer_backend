# ----- Build Stage -----
FROM mcr.microsoft.com/dotnet/sdk:9.0.100 AS build
WORKDIR /src

COPY ["AdhdTimeOrganizer.sln", "."]
COPY ["AdhdTimeOrganizer/AdhdTimeOrganizer.csproj", "AdhdTimeOrganizer/"]
RUN dotnet restore "AdhdTimeOrganizer.sln"

COPY . .
WORKDIR "/src/AdhdTimeOrganizer"
RUN dotnet build "AdhdTimeOrganizer.csproj" -c Release -o /app/build -p:RunAnalyzersDuringBuild=false

# ----- Publish Stage -----
FROM build AS publish
RUN dotnet publish "AdhdTimeOrganizer.csproj" -c Release -o /app/publish --no-build -p:RunAnalyzersDuringBuild=false

# ----- Runtime Stage -----
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

RUN adduser -u 5000 --disabled-password --gecos "" appuser && chown -R appuser /app
USER appuser

RUN mkdir -p /app/secrets && chmod 700 /app/secrets

COPY --from=publish /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "AdhdTimeOrganizer.dll"]