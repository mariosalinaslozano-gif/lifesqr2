# Render Environment Variables

Add these environment variables in your Render dashboard under **Environment > Environment Variables**:

| Key | Value |
|-----|-------|
| `OPENROUTER_API_KEY` | `<set-in-render-dashboard-only>` |
| `ConnectionStrings__DefaultConnection` | `Host=<internal-host>;Port=5432;Database=<database-name>;Username=<database-user>;Password=<database-password>` |
| `ASPNETCORE_URLS` | `http://+:80` |

## PostgreSQL Credentials

- **Host**: `<internal-host>`
- **Port**: `5432`
- **Database**: `<database-name>`
- **Username**: `<database-user>`
- **Password**: `<database-password>`

## Notes
- Use the **Internal Host** so the webapp and database communicate within Render's private network.

## Full Connection String
```
Host=<internal-host>;Port=5432;Database=<database-name>;Username=<database-user>;Password=<database-password>
```
