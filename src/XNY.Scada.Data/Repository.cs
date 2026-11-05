using SqlSugar;
using XNY.Scada.Domain.Entities;

namespace XNY.Scada.Data;

/// <summary>通用仓储，复杂查询直接使用 <see cref="Db"/></summary>
/// <param name="db">数据库客户端。</param>
public class Repository<T>(ISqlSugarClient db) where T : class, new()
{
    /// <summary>数据库。</summary>
    public ISqlSugarClient Db { get; } = db;

    /// <summary>查询。</summary>
    public ISugarQueryable<T> Query() => Db.Queryable<T>();

    /// <summary>获取全部。</summary>
    public Task<List<T>> GetAllAsync() => Db.Queryable<T>().ToListAsync();

    /// <summary>获取按标识。</summary>
    public Task<T> GetByIdAsync(long id) => Db.Queryable<T>().InSingleAsync(id);

    /// <summary>插入。</summary>
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

    /// <summary>更新。</summary>
    public Task<int> UpdateAsync(T entity)
    {
        if (entity is EntityBase eb) eb.UpdatedAt = DateTime.Now;
        return Db.Updateable(entity).ExecuteCommandAsync();
    }

    /// <summary>保存。</summary>
    public async Task<T> SaveAsync(T entity)
    {
        if (entity is EntityBase { Id: > 0 })
        {
            await UpdateAsync(entity);
            return entity;
        }
        return await InsertAsync(entity);
    }

    /// <summary>删除。</summary>
    public Task<int> DeleteAsync(long id) => Db.Deleteable<T>().In(id).ExecuteCommandAsync();
}
