using SqlSugar;
using XNY.Scada.Domain.Entities;

namespace XNY.Scada.Data;

/// <summary>通用仓储，复杂查询直接使用 <see cref="Db"/></summary>
public class Repository<T>(ISqlSugarClient db) where T : class, new()
{
    public ISqlSugarClient Db { get; } = db;

    public ISugarQueryable<T> Query() => Db.Queryable<T>();

    public Task<List<T>> GetAllAsync() => Db.Queryable<T>().ToListAsync();

    public Task<T> GetByIdAsync(long id) => Db.Queryable<T>().InSingleAsync(id);

    public async Task<T> InsertAsync(T entity)
    {
        if (entity is EntityBase eb)
        {
            eb.CreatedAt = eb.UpdatedAt = DateTime.Now;
            eb.Id = await Db.Insertable(entity).ExecuteReturnBigIdentityAsync();
            return entity;
        }
        await Db.Insertable(entity).ExecuteCommandAsync();
        return entity;
    }

    public Task<int> UpdateAsync(T entity)
    {
        if (entity is EntityBase eb) eb.UpdatedAt = DateTime.Now;
        return Db.Updateable(entity).ExecuteCommandAsync();
    }

    public async Task<T> SaveAsync(T entity)
    {
        if (entity is EntityBase { Id: > 0 })
        {
            await UpdateAsync(entity);
            return entity;
        }
        return await InsertAsync(entity);
    }

    public Task<int> DeleteAsync(long id) => Db.Deleteable<T>().In(id).ExecuteCommandAsync();
}
