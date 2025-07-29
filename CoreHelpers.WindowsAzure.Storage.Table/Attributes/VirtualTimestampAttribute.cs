using Azure.Data.Tables;
using CoreHelpers.WindowsAzure.Storage.Table.Extensions;
using CoreHelpers.WindowsAzure.Storage.Table.Serialization;
using System;
using System.Reflection;

namespace CoreHelpers.WindowsAzure.Storage.Table.Attributes
{
    
    [AttributeUsage(AttributeTargets.Property)]
    public class VirtualTimestampAttribute : Attribute, IVirtualTypeAttribute
    {

        void IVirtualTypeAttribute.ReadProperty<T>(TableEntity dataObject, PropertyInfo propertyInfo, T obj)
        {
            propertyInfo.SetDateTimeOffsetValue(obj, dataObject.Timestamp);
        }

        public void WriteProperty<T>(PropertyInfo propertyInfo, T obj, TableEntityBuilder builder)
        {
            // do nothing as we cannot change the TimeStamp property
        }
    }
    
}
