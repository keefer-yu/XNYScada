using SqlSugar;

namespace XNY.Scada.Domain.Entities;

/// <summary>持久化实体公共字段：自增主键与创建、更新时间。</summary>
public abstract class EntityBase
{
    /// <summary>实体基类标识。</summary>
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
    public long Id { get; set; }

    /// <summary>创建时间。</summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>更新时间。</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}
