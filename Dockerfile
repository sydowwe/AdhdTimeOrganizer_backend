FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["AdhdTimeOrganizer/AdhdTimeOrganizer.csproj", "AdhdTimeOrganizer/"]
RUN dotnet restore "AdhdTimeOrganizer/AdhdTimeOrganizer.csproj"

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
# EcdsaKeyProvider reads this as a raw env var (no fallback) and throws if it is unset.
# No .env is copied into the image, so it has to be set here.
ENV ECDSA_PRIVATE_KEY_PATH=secrets/ec_private.pem

# REQUIRED AT RUNTIME, and deliberately NOT set here: FIELD_ENCRYPTION_KEY
#
# Base64-encoded 32-byte AES-256 key backing EncryptedColumn (User.GoogleCalendarRefreshToken,
# DesktopActivityEntry.ExecutablePath). It is resolved during OnModelCreating, so the container will
# not start without it -- Program.EnsureFieldEncryptionKey fails the boot with an explicit message.
#
# It is absent from this file on purpose: an ENV line here would commit a live encryption key to the
# repository. Supply it from the container runtime instead -- `docker run -e FIELD_ENCRYPTION_KEY=...`,
# a compose `environment:` entry, or an orchestrator secret.
#
# Generate one with:
#   [Convert]::ToBase64String([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
#
# Use a DIFFERENT key per environment, and note that rotating it makes every existing `enc:v1:` token
# undecryptable -- there is no re-encryption tooling yet.
EXPOSE 8080
ENTRYPOINT ["dotnet", "AdhdTimeOrganizer.dll"]