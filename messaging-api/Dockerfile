FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app


COPY *.csproj nuget.config ./
RUN dotnet restore messaging.api.csproj


COPY . ./
RUN dotnet publish messaging.api.csproj -c Release -o /out


FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# curl para o HEALTHCHECK: a imagem de runtime do .NET nao traz curl nem wget.
RUN apt-get update \
  && apt-get install -y --no-install-recommends curl \
  && rm -rf /var/lib/apt/lists/*

COPY --from=build /out .

# A imagem aspnet:8.0 escuta em 8080 por padrao, mas o compose publica 6006:6006 e
# o HEALTHCHECK procura em 6006. Fixar aqui evita depender de ASPNETCORE_URLS vir
# certo em cada ambiente - se vier, sobrescreve este valor.
ENV ASPNETCORE_URLS=http://+:6006

EXPOSE 6006

# Readiness: /api/health/ready confirma a conexao com o RabbitMQ.
HEALTHCHECK --interval=30s --timeout=10s --start-period=40s --retries=3 \
  CMD curl -fsS "http://127.0.0.1:${HEALTH_PORT:-6006}/api/health/ready" || exit 1

ENTRYPOINT ["dotnet", "messaging.api.dll"]
