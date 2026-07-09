FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish decision-service/src/DecisionService/DecisionService.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app .
# Trusts the NetFree content-filtering CA used on the dev network — without this,
# outbound HTTPS calls to the real Gemini API fail with a PartialChain TLS error,
# since NetFree re-signs HTTPS traffic and only the host machine trusts its root cert.
COPY decision-service/certs/netfree-bezeq-root.crt /usr/local/share/ca-certificates/netfree-bezeq-root.crt
RUN update-ca-certificates
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "DecisionService.dll"]
