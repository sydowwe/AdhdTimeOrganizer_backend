FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["AdhdTimeOrganizer.sln", "."]
COPY ["AdhdTimeOrganizer/AdhdTimeOrganizer.csproj", "AdhdTimeOrganizer/"]
RUN dotnet restore "AdhdTimeOrganizer.sln"

COPY . .
WORKDIR "/src/AdhdTimeOrganizer"
RUN dotnet publish "AdhdTimeOrganizer.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN useradd -u 5000 -M -s /sbin/nologin appuser && chown -R appuser /app
USER appuser

RUN mkdir -p /app/secrets && chmod 700 /app/secrets

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "AdhdTimeOrganizer.dll"]