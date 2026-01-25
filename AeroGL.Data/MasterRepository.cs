using AeroGL.Core;
using Dapper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AeroGL.Data
{
    public sealed class MasterRepository : ICompanyRepository
    {
        public async Task<List<Company>> GetAll()
        {
            using (var cn = Db.OpenMaster()) // Selalu tembak ke master.db
            {
                var sql = "SELECT * FROM Companies ORDER BY Name";
                var rows = await cn.QueryAsync<Company>(sql);
                return rows.ToList();
            }
        }

        public async Task<Company> GetById(Guid id)
        {
            using (var cn = Db.OpenMaster())
            {
                var sql = "SELECT * FROM Companies WHERE Id = @id";
                return await cn.QueryFirstOrDefaultAsync<Company>(sql, new { id = id.ToString() });
            }
        }

        public async Task Upsert(Company c)
        {
            using (var cn = Db.OpenMaster()) //
            {
                const string sql = @"
            INSERT INTO Companies (Id, Name, Password, DbPath, CreatedDate, LastAccessed, IsActive)
            VALUES (@Id, @Name, @Password, @DbPath, @CreatedDate, @LastAccessed, @IsActive)
            ON CONFLICT(Id) DO UPDATE SET 
                Name = @Name, 
                Password = @Password,
                DbPath = @DbPath, 
                LastAccessed = @LastAccessed, 
                IsActive = @IsActive;";

                await cn.ExecuteAsync(sql, new
                {
                    Id = c.Id.ToString(),
                    c.Name,
                    c.Password,
                    c.DbPath,
                    CreatedDate = c.CreatedDate.ToString("yyyy-MM-dd HH:mm:ss"),
                    LastAccessed = c.LastAccessed?.ToString("yyyy-MM-dd HH:mm:ss"),
                    IsActive = c.IsActive ? 1 : 0
                });
            }
        }

        public async Task Delete(Guid id)
        {
            using (var cn = Db.OpenMaster())
            {
                await cn.ExecuteAsync("DELETE FROM Companies WHERE Id = @id", new { id = id.ToString() });
            }
        }

        public async Task<Company> GetActive()
        {
            using (var cn = Db.OpenMaster())
            {
                return await cn.QueryFirstOrDefaultAsync<Company>(
                    "SELECT * FROM Companies WHERE IsActive = 1 LIMIT 1");
            }
        }

        public async Task MarkAsActive(Guid id)
        {
            using (var cn = Db.OpenMaster())
            {
                // Reset semua dulu, baru set satu yang aktif
                await cn.ExecuteAsync("UPDATE Companies SET IsActive = 0");
                await cn.ExecuteAsync("UPDATE Companies SET IsActive = 1 WHERE Id = @id", new { id = id.ToString() });
            }
        }
    }
}