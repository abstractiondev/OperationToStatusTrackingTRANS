using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using System.Xml.Serialization;
using Operation_v1_0;
using StatusTracking_v1_0;

namespace OperationToStatusTrackingTRANS
{
    public class Transformer
    {
        T LoadXml<T>(string xmlFileName)
        {
            using (FileStream fStream = File.OpenRead(xmlFileName))
            {
                XmlSerializer serializer = new XmlSerializer(typeof(T));
                T result = (T)serializer.Deserialize(fStream);
                fStream.Close();
                return result;
            }
        }



	    public Tuple<string, string>[] GetGeneratorContent(params string[] xmlFileNames)
	    {
            List<Tuple<string, string>> result = new List<Tuple<string, string>>();
            foreach(string xmlFileName in xmlFileNames)
            {
                OperationAbstractionType fromAbs = LoadXml<OperationAbstractionType>(xmlFileName);
                StatusTrackingAbstractionType toAbs = TransformAbstraction(fromAbs);
                string xmlContent = WriteToXmlString(toAbs);
                FileInfo fInfo = new FileInfo(xmlFileName);
                string contentFileName = "StatusTracking_From" + fInfo.Name;
                result.Add(Tuple.Create(contentFileName, xmlContent));
            }
	        return result.ToArray();
	    }

        private string WriteToXmlString(object toAbs)
        {
            XmlSerializer serializer = new XmlSerializer(toAbs.GetType());
            MemoryStream memoryStream = new MemoryStream();
            serializer.Serialize(memoryStream, toAbs);
            byte[] data = memoryStream.ToArray();
            string result = System.Text.Encoding.UTF8.GetString(data);
            return result;
        }

        public static StatusTrackingAbstractionType TransformAbstraction(OperationAbstractionType fromAbs)
        {
            StatusTrackingAbstractionType toAbs = new StatusTrackingAbstractionType
                                                      {
                                                         Groups = fromAbs.Operations.SelectMany(opers => opers.Operation).Select(GetStatusGroup).ToArray(),
                                                         StatusItems = fromAbs.Operations.SelectMany(opers => opers.Operation).SelectMany(GetStatusItems).ToArray()
            };
            CleanupMissingGroupRefs(toAbs);
            return toAbs;
        }

        private static void CleanupMissingGroupRefs(StatusTrackingAbstractionType toAbs)
        {
            foreach(var grp in toAbs.Groups)
            {
                grp.GroupRef =
                    grp.GroupRef.Where(
                        groupRef => toAbs.Groups.Count(existingGroup => groupRef.groupName == existingGroup.name) > 0).
                        ToArray();
            }
        }

        private static StatusItemType[] GetStatusItems(OperationType operation)
        {
            List<StatusItemType> result = new List<StatusItemType>();
            if (operation.Parameters != null)
            {
                result.AddRange(operation.Parameters.Parameter.Select(GetDefaultStatusItem));
                result.AddRange(operation.Parameters.Items.Select(GetDefaultStatusItem));
            }

            result.AddRange(operation.Execution.SequentialExecution.Select(GetDefaultStatusItem));
            result.AddRange(
                (operation.OperationReturnValues ?? new OperationReturnValuesType {ReturnValue = new VariableType[0]}).
                    ReturnValue.Select(GetDefaultStatusItem));
            result.ForEach(statusItem => statusItem.name = operation.name + "_" + statusItem.name);
            return result.ToArray();
        }

        private static StatusItemType GetDefaultStatusItem(object objWithNameAndState)
        {
            string displayNamePrefix = objWithNameAndState.GetType().Name + ": ";
            return GetStatusItemType("", displayNamePrefix, objWithNameAndState);
        }

        private static StatusItemType GetStatusItemType(string prefixName, string displayNamePrefix, object objWithNameAndState, decimal difficultyFactor = 1)
        {
            dynamic dynObj = objWithNameAndState;
            return new StatusItemType
                       {
                           name = prefixName + dynObj.name,
                           displayName = displayNamePrefix + dynObj.name,
                           description = dynObj.designDesc,
                           StatusValue = new StatusValueType
                                             {
                                                 indicatorValue = difficultyFactor,
                                                 indicatorDisplayText = GetIndicatorDisplayText(dynObj.state),
                                                 trafficLightIndicator = GetTrafficLightIndicator(dynObj.state)
                                             }
                       };
        }

        private static string GetIndicatorDisplayText(VariableTypeState state)
        {
            switch(state)
            {
                case VariableTypeState.underDesign:
                    return "Under design";
                case VariableTypeState.designApproved:
                    return "Being implemented";
                case VariableTypeState.implemented:
                    return "Implemented";
                default:
                    throw new NotSupportedException("VariableTypeState value: " + state);
            }
        }

        private static StatusValueTypeTrafficLightIndicator GetTrafficLightIndicator(VariableTypeState state)
        {
            switch(state)
            {
                case VariableTypeState.underDesign:
                    return StatusValueTypeTrafficLightIndicator.red;
                case VariableTypeState.designApproved:
                    return StatusValueTypeTrafficLightIndicator.yellow;
                case VariableTypeState.implemented:
                    return StatusValueTypeTrafficLightIndicator.green;
                default:
                    throw new NotSupportedException("Variabletype state: " + state);
            }
        }

        private static GroupType GetStatusGroup(OperationType operation)
        {
            StatusItemType[] items = GetStatusItems(operation);
            ItemRefType[] statusItemRefs = items.Select(item => new ItemRefType {itemName = item.name}).ToArray();
            GroupRefType[] groupRefs = operation.Execution.SequentialExecution
                .Select(exec => exec as OperationExecuteType)
                .Where(opexec => opexec != null)
                .Select(opexec => new GroupRefType {groupName = opexec.targetOperationName}).ToArray();
            GroupType result = new GroupType()
                                   {
                                       name = operation.name,
                                       groupRole = operation.isRootOperation ? GroupTypeGroupRole.Root : GroupTypeGroupRole.None,
                                       ItemRef = statusItemRefs,
                                       GroupRef = groupRefs
                                   };
            return result;
        }
    }
}
