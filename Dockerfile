FROM mcr.microsoft.com/dotnet/aspnet:10.0.9-noble-chiseled

ENV ASPNETCORE_HTTP_PORTS=19423 \
    ShadowDrop__Metadata__LiteDbPath=/app/data/metadata/shadowdrop.db \
    ShadowDrop__Storage__LocalRoot=/app/data/storage/

EXPOSE 19423

COPY --chown=$APP_UID:$APP_UID artifacts/publish/api/ /app/
COPY --chown=$APP_UID:$APP_UID --chmod=700 docker/app-data/ /app/data/

VOLUME ["/app/data"]

USER $APP_UID
WORKDIR /app

ENTRYPOINT ["dotnet", "ShadowDrop.Api.dll"]
