FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src
COPY REvolutionClient.Auth.csproj ./
RUN dotnet restore REvolutionClient.Auth.csproj
COPY . ./
RUN dotnet publish REvolutionClient.Auth.csproj --configuration Release --no-restore --output /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime
USER root
RUN sed -i -e 's|http://|https://|g' -e 's/^Components:.*/Components: main/' /etc/apt/sources.list.d/ubuntu.sources \
    && apt-get -o Acquire::By-Hash=force -o Acquire::Retries=3 update \
    && apt-get install --yes --no-install-recommends curl sqlite3 \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /data \
    && chown app:app /data
WORKDIR /app
COPY --from=build /out ./
ENV REVOLUTION_AUTH_URL=http://0.0.0.0:25567 \
    REVOLUTION_AUTH_DB=/data/auth.db
EXPOSE 25567
USER app
ENTRYPOINT ["dotnet", "REvolutionClient.Auth.dll"]
