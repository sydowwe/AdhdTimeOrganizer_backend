# ---------- build stage ----------
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Now copy the rest of the source code
COPY . .

# Build and publish the main project
WORKDIR /src/MojaDigitalnaFirma.Sandbox.AdminPortal
RUN dotnet publish -c Release -o /out \
    -p:PublishReadyToRun=true

# ---------- runtime stage ----------
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime

WORKDIR /app
RUN mkdir -p /app/secrets && chmod 500 /app/secrets
COPY --from=build /out ./

# Update the entry point to match your actual assembly name
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "MojaDigitalnaFirma.Sandbox.AdminPortal.dll"]