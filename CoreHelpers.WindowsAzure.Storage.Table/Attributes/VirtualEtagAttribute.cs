using Azure.Data.Tables;
using CoreHelpers.WindowsAzure.Storage.Table.Serialization;
using System;
using System.Reflection;

namespace CoreHelpers.WindowsAzure.Storage.Table.Attributes
{
    [AttributeUsage(AttributeTargets.Property)]
    public class VirtualEtagAttribute : Attribute, IVirtualTypeAttribute
    {
        public void ReadProperty<T>(TableEntity entity, PropertyInfo property, T model)
        {            
            property.SetValue(model, entity.ETag.ToString());

        }

        public void WriteProperty<T>(PropertyInfo propertyInfo, T obj, TableEntityBuilder builder)
        {
            var value = propertyInfo.GetValue(obj);

            if (value is string valueStr && !string.IsNullOrWhiteSpace(valueStr))
                builder.ETag = new Azure.ETag(valueStr);
        }
    }
}
