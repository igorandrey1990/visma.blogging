FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Visma.Blogging.slnx ./
COPY src/Visma.Blogging.Domain/Visma.Blogging.Domain.csproj src/Visma.Blogging.Domain/
COPY src/Visma.Blogging.Application/Visma.Blogging.Application.csproj src/Visma.Blogging.Application/
COPY src/Visma.Blogging.Infrastructure/Visma.Blogging.Infrastructure.csproj src/Visma.Blogging.Infrastructure/
COPY src/Visma.Blogging.Api/Visma.Blogging.Api.csproj src/Visma.Blogging.Api/
RUN dotnet restore src/Visma.Blogging.Api/Visma.Blogging.Api.csproj

COPY src/ src/
RUN dotnet publish src/Visma.Blogging.Api/Visma.Blogging.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "Visma.Blogging.Api.dll"]
