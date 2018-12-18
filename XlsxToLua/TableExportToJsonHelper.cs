using LitJson;
using System;
using System.Collections.Generic;
using System.Text;

public class TableExportToJsonHelper
{
    // 用于缩进json的字符串
    private static string _JSON_INDENTATION_STRING = "\t";

    public static bool ExportTableToJson(TableInfo tableInfo, out string errorString)
    {
        StringBuilder content = new StringBuilder();

        // 若生成为各行数据对应的json object包含在一个json array的形式
        if (AppValues.ExportJsonIsExportJsonArrayFormat == true)
        {
            // 生成json字符串开头，每行数据为一个json object，作为整张表json array的元素
            content.Append("[");

            // 逐行读取表格内容生成json
            List<FieldInfo> allField = tableInfo.GetAllClientFieldInfo();
            int dataCount = tableInfo.GetKeyColumnFieldInfo().Data.Count;
            int fieldCount = allField.Count;
            for (int row = 0; row < dataCount; ++row)
            {
                // 生成一行数据json object的开头
                content.Append("{");

                for (int column = 0; column < fieldCount; ++column)
                {
                    string oneFieldString = _GetOneField(allField[column], row, out errorString);
                    if (errorString != null)
                    {
                        errorString = string.Format("额外导出表格{0}为json文件失败，", tableInfo.TableName) + errorString;
                        return false;
                    }
                    else
                        content.Append(oneFieldString);
                }

                // 去掉本行最后一个字段后多余的英文逗号，json语法不像lua那样最后一个字段后的逗号可有可无
                content.Remove(content.Length - 1, 1);
                // 生成一行数据json object的结尾
                content.Append("}");
                // 每行的json object后加英文逗号
                content.Append(",");
            }

            // 去掉最后一行后多余的英文逗号，此处要特殊处理当表格中没有任何数据行时的情况
            if (content.Length > 1)
                content.Remove(content.Length - 1, 1);
            // 生成json字符串结尾
            content.Append("]");
        }
        else
        {
            // 生成json字符串开头，每行数据以表格主键列为key，各字段信息组成的json object为value，作为整张表json object的元素
            content.Append("{");

            // 逐行读取表格内容生成json
            List<FieldInfo> allField = tableInfo.GetAllClientFieldInfo();
            FieldInfo keyColumnInfo = tableInfo.GetKeyColumnFieldInfo();
            int dataCount = keyColumnInfo.Data.Count;
            int fieldCount = allField.Count;
            for (int row = 0; row < dataCount; ++row)
            {
                // 将主键列的值作为key
                string keyString = null;
                if (keyColumnInfo.DataType == DataType.String)
                {
                    keyString = _GetStringValue(keyColumnInfo, row);
                    content.Append(keyString);
                }
                else if (keyColumnInfo.DataType == DataType.Int || keyColumnInfo.DataType == DataType.Long)
                {
                    keyString = _GetNumberValue(keyColumnInfo, row);
                    content.Append("\"").Append(keyString).Append("\"");
                }
                else
                {
                    errorString = string.Format("ExportTableToJson函数中未定义{0}类型的主键数值导出至json文件的形式", keyColumnInfo.DataType);
                    Utils.LogErrorAndExit(errorString);
                    return false;
                }

                // 生成一行数据json object的开头
                content.Append(":{");

                int startColumn = (AppValues.ExportJsonIsExportJsonMapIncludeKeyColumnValue == true ? 0 : 1);
                for (int column = startColumn; column < fieldCount; ++column)
                {
                    string oneFieldString = _GetOneField(allField[column], row, out errorString);
                    if (errorString != null)
                    {
                        errorString = string.Format("额外导出表格{0}为json文件失败，", tableInfo.TableName) + errorString;
                        return false;
                    }
                    else
                        content.Append(oneFieldString);
                }

                // 去掉本行最后一个字段后多余的英文逗号，json语法不像lua那样最后一个字段后的逗号可有可无
                content.Remove(content.Length - 1, 1);
                // 生成一行数据json object的结尾
                content.Append("}");
                // 每行的json object后加英文逗号
                content.Append(",");
            }

            // 去掉最后一行后多余的英文逗号，此处要特殊处理当表格中没有任何数据行时的情况
            if (content.Length > 1)
                content.Remove(content.Length - 1, 1);
            // 生成json字符串结尾
            content.Append("}");
        }

        string exportString = content.ToString();

        // 如果声明了要整理为带缩进格式的形式
        if (AppValues.ExportJsonIsFormat == true)
            exportString = _FormatJson(exportString);

        // 保存为json文件
        if (Utils.SaveJsonFile(tableInfo.TableName, exportString) == true)
        {
            errorString = null;
            return true;
        }
        else
        {
            errorString = "保存为json文件失败\n";
            return false;
        }
    }

    /// <summary>
    /// 按配置的特殊索引导出方式输出json文件（如果声明了在生成的json文件开头以注释形式展示列信息，将生成更直观的嵌套字段信息，而不同于普通导出规则的列信息展示）
    /// </summary>
    public static bool SpecialExportTableToJson(TableInfo tableInfo, string exportRule, out string errorString)
    {
        exportRule = exportRule.Trim();
        // 解析按这种方式导出后的json文件名
        int colonIndex = exportRule.IndexOf(':');
        if (colonIndex == -1)
        {
            errorString = string.Format("导出配置\"{0}\"定义错误，必须在开头声明导出lua文件名\n", exportRule);
            return false;
        }
        string fileName = exportRule.Substring(0, colonIndex).Trim();
        // 判断是否在最后的花括号内声明table value中包含的字段
        int leftBraceIndex = exportRule.LastIndexOf('{');
        int rightBraceIndex = exportRule.LastIndexOf('}');
        // 解析依次作为索引的字段名
        string indexFieldNameString = null;
        // 注意分析花括号时要考虑到未声明table value中的字段而在某索引字段完整性检查规则中用花括号声明了有效值的情况
        if (exportRule.EndsWith("}") && leftBraceIndex != -1)
            indexFieldNameString = exportRule.Substring(colonIndex + 1, leftBraceIndex - colonIndex - 1);
        else
            indexFieldNameString = exportRule.Substring(colonIndex + 1, exportRule.Length - colonIndex - 1);

        string[] indexFieldDefine = indexFieldNameString.Split(new char[] { '-' }, System.StringSplitOptions.RemoveEmptyEntries);
        // 用于索引的字段列表
        List<FieldInfo> indexField = new List<FieldInfo>();
        // 索引字段对应的完整性检查规则
        List<string> integrityCheckRules = new List<string>();
        if (indexFieldDefine.Length < 1)
        {
            errorString = string.Format("导出配置\"{0}\"定义错误，用于索引的字段不能为空，请按fileName:indexFieldName1-indexFieldName2{{otherFieldName1,otherFieldName2}}的格式配置\n", exportRule);
            return false;
        }
        // 检查字段是否存在且为int、float、string或lang型
        foreach (string fieldDefine in indexFieldDefine)
        {
            string fieldName = null;
            // 判断是否在字段名后用小括号声明了该字段的完整性检查规则
            int leftBracketIndex = fieldDefine.IndexOf('(');
            int rightBracketIndex = fieldDefine.IndexOf(')');
            if (leftBracketIndex > 0 && rightBracketIndex > leftBracketIndex)
            {

                fieldName = fieldDefine.Substring(0, leftBracketIndex);
                string integrityCheckRule = fieldDefine.Substring(leftBracketIndex + 1, rightBracketIndex - leftBracketIndex - 1).Trim();
                if (string.IsNullOrEmpty(integrityCheckRule))
                {
                    errorString = string.Format("导出配置\"{0}\"定义错误，用于索引的字段\"{1}\"若要声明完整性检查规则就必须在括号中填写否则不要加括号\n", exportRule, fieldName);
                    return false;
                }
                integrityCheckRules.Add(integrityCheckRule);
            }
            else
            {
                fieldName = fieldDefine.Trim();
                integrityCheckRules.Add(null);
            }

            FieldInfo fieldInfo = tableInfo.GetFieldInfoByFieldName(fieldName);
            if (fieldInfo == null)
            {
                errorString = string.Format("导出配置\"{0}\"定义错误，声明的索引字段\"{1}\"不存在\n", exportRule, fieldName);
                return false;
            }
            if (fieldInfo.DataType != DataType.Int && fieldInfo.DataType != DataType.Long && fieldInfo.DataType != DataType.Float && fieldInfo.DataType != DataType.String && fieldInfo.DataType != DataType.Lang)
            {
                errorString = string.Format("导出配置\"{0}\"定义错误，声明的索引字段\"{1}\"为{2}型，但只允许为int、long、float、string或lang型\n", exportRule, fieldName, fieldInfo.DataType);
                return false;
            }

            // 对索引字段进行非空检查
            if (fieldInfo.DataType == DataType.String)
            {
                FieldCheckRule stringNotEmptyCheckRule = new FieldCheckRule();
                stringNotEmptyCheckRule.CheckType = TableCheckType.NotEmpty;
                stringNotEmptyCheckRule.CheckRuleString = "notEmpty[trim]";
                TableCheckHelper.CheckNotEmpty(fieldInfo, stringNotEmptyCheckRule, out errorString);
                if (errorString != null)
                {
                    errorString = string.Format("按配置\"{0}\"进行自定义导出错误，string型索引字段\"{1}\"中存在以下空值，而作为索引的key不允许为空\n{2}\n", exportRule, fieldName, errorString);
                    return false;
                }
            }
            else if (fieldInfo.DataType == DataType.Lang)
            {
                FieldCheckRule langNotEmptyCheckRule = new FieldCheckRule();
                langNotEmptyCheckRule.CheckType = TableCheckType.NotEmpty;
                langNotEmptyCheckRule.CheckRuleString = "notEmpty[key|value]";
                TableCheckHelper.CheckNotEmpty(fieldInfo, langNotEmptyCheckRule, out errorString);
                if (errorString != null)
                {
                    errorString = string.Format("按配置\"{0}\"进行自定义导出错误，lang型索引字段\"{1}\"中存在以下空值，而作为索引的key不允许为空\n{2}\n", exportRule, fieldName, errorString);
                    return false;
                }
            }
            else if (AppValues.IsAllowedNullNumber == true)
            {
                FieldCheckRule numberNotEmptyCheckRule = new FieldCheckRule();
                numberNotEmptyCheckRule.CheckType = TableCheckType.NotEmpty;
                numberNotEmptyCheckRule.CheckRuleString = "notEmpty";
                TableCheckHelper.CheckNotEmpty(fieldInfo, numberNotEmptyCheckRule, out errorString);
                if (errorString != null)
                {
                    errorString = string.Format("按配置\"{0}\"进行自定义导出错误，{1}型索引字段\"{2}\"中存在以下空值，而作为索引的key不允许为空\n{3}\n", exportRule, fieldInfo.DataType.ToString(), fieldName, errorString);
                    return false;
                }
            }

            indexField.Add(fieldInfo);
        }
        // 解析table value中要输出的字段名
        List <FieldInfo> tableValueField = new List<FieldInfo>();
        // 如果在花括号内配置了json value中要输出的字段名
        if (exportRule.EndsWith("}") && leftBraceIndex != -1 && leftBraceIndex < rightBraceIndex)
        {
            string tableValueFieldName = exportRule.Substring(leftBraceIndex + 1, rightBraceIndex - leftBraceIndex - 1);
            string[] fieldNames = tableValueFieldName.Split(new char[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (fieldNames.Length < 1)
            {
                errorString = string.Format("导出配置\"{0}\"定义错误，花括号中声明的table value中的字段不能为空，请按fileName:indexFieldName1-indexFieldName2{{otherFieldName1,otherFieldName2}}的格式配置\n", exportRule);
                return false;
            }
            // 检查字段是否存在
            foreach (string fieldName in fieldNames)
            {
                FieldInfo fieldInfo = tableInfo.GetFieldInfoByFieldName(fieldName);
                if (fieldInfo == null)
                {
                    errorString = string.Format("导出配置\"{0}\"定义错误，声明的table value中的字段\"{1}\"不存在\n", exportRule, fieldName);
                    return false;
                }

                if (tableValueField.Contains(fieldInfo))
                    Utils.LogWarning(string.Format("警告：导出配置\"{0}\"定义中，声明的table value中的字段存在重复，字段名为{1}（列号{2}），本工具只生成一次，请修正错误\n", exportRule, fieldName, Utils.GetExcelColumnName(fieldInfo.ColumnSeq + 1)));
                else
                    tableValueField.Add(fieldInfo);
            }
        }
        else if (exportRule.EndsWith("}") && leftBraceIndex == -1)
        {
            errorString = string.Format("导出配置\"{0}\"定义错误，声明的table value中花括号不匹配\n", exportRule);
            return false;
        }
        // 如果未在花括号内声明，则默认将索引字段之外的所有字段进行填充
        else
        {
            List<string> indexFieldNameList = new List<string>(indexFieldDefine);
            foreach (FieldInfo fieldInfo in tableInfo.GetAllClientFieldInfo())
            {
                if (!indexFieldNameList.Contains(fieldInfo.FieldName))
                    tableValueField.Add(fieldInfo);
            }
        }

        // 解析完依次作为索引的字段以及table value中包含的字段后，按索引要求组成相应的嵌套数据结构
        Dictionary<object, object> data = new Dictionary<object, object>();
        int rowCount = tableInfo.GetKeyColumnFieldInfo().Data.Count;
        for (int i = 0; i < rowCount; ++i)
        {
            Dictionary<object, object> temp = data;
            // 生成除最内层的数据结构
            for (int j = 0; j < indexField.Count - 1; ++j)
            {
                FieldInfo oneIndexField = indexField[j];
                var tempData = oneIndexField.Data[i];
                if (!temp.ContainsKey(tempData))
                    temp.Add(tempData, new Dictionary<object, object>());

                temp = (Dictionary<object, object>)temp[tempData];
            }
            // 最内层的value存数据的int型行号（从0开始计）
            FieldInfo lastIndexField = indexField[indexField.Count - 1];
            var lastIndexFieldData = lastIndexField.Data[i];
            if (!temp.ContainsKey(lastIndexFieldData))
                temp.Add(lastIndexFieldData, i);
            else
            {
                errorString = string.Format("错误：对表格{0}按\"{1}\"规则进行特殊索引导出时发现第{2}行与第{3}行在各个索引字段的值完全相同，导出被迫停止，请修正错误后重试\n", tableInfo.TableName, exportRule, i + AppValues.DATA_FIELD_DATA_START_INDEX + 1, temp[lastIndexFieldData]);
                Utils.LogErrorAndExit(errorString);
                return false;
            }
        }
        // 进行数据完整性检查
        if (AppValues.IsNeedCheck == true)
        {
            TableCheckHelper.CheckTableIntegrity(indexField, data, integrityCheckRules, out errorString);
            if (errorString != null)
            {
                errorString = string.Format("错误：对表格{0}按\"{1}\"规则进行特殊索引导时未通过数据完整性检查，导出被迫停止，请修正错误后重试：\n{2}\n", tableInfo.TableName, exportRule, errorString);
                return false;
            }
        }

        // 生成导出的文件内容
        StringBuilder content = new StringBuilder();

        // 生成数据内容开头
        content.AppendLine("{");

        // 当前缩进量
        int currentLevel = 1;

        // 逐层按嵌套结构输出数据
        _GetIndexFieldData(content, data, tableValueField, ref currentLevel, out errorString);
       
        if (errorString != null)
        {
            errorString = string.Format("错误：对表格{0}按\"{1}\"规则进行特殊索引导出时发现以下错误，导出被迫停止，请修正错误后重试：\n{2}\n", tableInfo.TableName, exportRule, errorString);
            return false;
        }
        // 去掉最后一行后多余的英文逗号，此处要特殊处理当表格中没有任何数据行时的情况
       if (content.Length > 1)
        {
            content.Remove(content.Length - 1, 1);
        }
        // 生成数据内容结尾
        content.AppendLine("}");
        string exportString = _ToTightJsonString(content.ToString());
        // 如果声明了要整理为带缩进格式的形式
        if (AppValues.ExportJsonIsFormat == true){
            exportString = _FormatJson(exportString);
        }

        // 保存为json文件
        if (Utils.SaveJsonFile(tableInfo.TableName, exportString) == true)
        {
            errorString = null;
            return true;
        }
        else
        {
            errorString = "保存为json文件失败\n";
            return false;
        }
    }

    /// <summary>
    /// 按指定索引方式导出数据时,通过此函数递归生成层次结构,当递归到最内层时输出指定object value中的数据
    /// </summary>
    private static void _GetIndexFieldData(StringBuilder content, Dictionary<object, object> parentDict, List<FieldInfo> tableValueField, ref int currentLevel, out string errorString)
    {
        int keyCount = 0;
        foreach (var key in parentDict.Keys)
        {
            content.Append(_GetJsonIndentation(currentLevel));
            // 生成key
            if (key.GetType() == typeof(int) || key.GetType() == typeof(float))
                content.Append("\"").Append(key).Append("\"");
            else if (key.GetType() == typeof(string))
            {
                //// 检查作为key值的变量名是否合法
                //TableCheckHelper.CheckFieldName(key.ToString(), out errorString);
                //if (errorString != null)
                //{
                //    errorString = string.Format("作为第{0}层索引的key值不是合法的变量名，你填写的为\"{1}\"", currentLevel - 1, key.ToString());
                //    return;
                //}
                //content.Append(key);

                content.Append("\"").Append(key).Append("\"");
            }
            else
            {
                errorString = string.Format("SpecialExportTableToJson中出现非法类型的索引列类型{0}", key.GetType());
                Utils.LogErrorAndExit(errorString);
                return;
            }

            content.AppendLine(":{");
            ++currentLevel;
            // 如果已是最内层，输出指定object value中的数据
            if (parentDict[key].GetType() == typeof(int))
            {
                foreach (FieldInfo fieldInfo in tableValueField)
                {
                    int rowIndex = (int)parentDict[key];
                    string oneTableValueFieldData = _GetOneField(fieldInfo, rowIndex,out errorString);
                    if (errorString != null)
                    {
                        errorString = string.Format("第{0}行的字段\"{1}\"（列号：{2}）导出数据错误：{3}", rowIndex + AppValues.DATA_FIELD_DATA_START_INDEX + 1, fieldInfo.FieldName, Utils.GetExcelColumnName(fieldInfo.ColumnSeq + 1), errorString);
                        return;
                    }
                    else
                    {
                        content.Append(_GetJsonIndentation(currentLevel));
                        content.Append(oneTableValueFieldData);
                    }
                }
            }
            else// 否则继续递归生成索引key
            {
                _GetIndexFieldData(content, (Dictionary<object, object>)(parentDict[key]), tableValueField, ref currentLevel, out errorString);
                if (errorString != null)
                    return;
            }

            --currentLevel;
            // 去掉本行最后一个字段后多余的英文逗号，json语法不像lua那样最后一个字段后的逗号可有可无
            content.Remove(content.Length - 1, 1);
            content.Append(_GetJsonIndentation(currentLevel));
            content.AppendLine("}");
            keyCount++;
            if (keyCount < parentDict.Count)//不是最后一层要加“，”
            {
                content.AppendLine(",");
            }
        }

        errorString = null;
    }


    private static string _GetOneField(FieldInfo fieldInfo, int row, out string errorString)
    {
        StringBuilder content = new StringBuilder();
        errorString = null;

        // 变量名，注意array下属的子元素在json中不含key的声明
        if (!(fieldInfo.ParentField != null && fieldInfo.ParentField.DataType == DataType.Array))
        {
            content.Append("\"").Append(fieldInfo.FieldName).Append("\"");
            content.Append(":");
        }

        // 对应数据值
        string value = null;
        switch (fieldInfo.DataType)
        {
            case DataType.Int:
            case DataType.Long:
            case DataType.Float:
                {
                    value = _GetNumberValue(fieldInfo, row);
                    break;
                }
            case DataType.String:
                {
                    value = _GetStringValue(fieldInfo, row);
                    break;
                }
            case DataType.Bool:
                {
                    value = _GetBoolValue(fieldInfo, row);
                    break;
                }
            case DataType.Lang:
                {
                    value = _GetLangValue(fieldInfo, row);
                    break;
                }
            case DataType.Date:
                {
                    value = _GetDateValue(fieldInfo, row);
                    break;
                }
            case DataType.Time:
                {
                    value = _GetTimeValue(fieldInfo, row);
                    break;
                }
            case DataType.Json:
                {
                    value = _GetJsonValue(fieldInfo, row);
                    break;
                }
            case DataType.TableString:
                {
                    value = _GetTableStringValue(fieldInfo, row, out errorString);
                    break;
                }
            case DataType.MapString:
                {
                    value = _GetMapStringValue(fieldInfo, row);
                    break;
                }
            case DataType.Dict:
                {
                    value = _GetDictValue(fieldInfo, row, out errorString);
                    break;
                }
            case DataType.Array:
                {
                    value = _GetArrayValue(fieldInfo, row, out errorString);
                    break;
                }
            default:
                {
                    errorString = string.Format("_GetOneField函数中未定义{0}类型数据导出至json文件的形式", fieldInfo.DataType);
                    Utils.LogErrorAndExit(errorString);
                    return null;
                }
        }

        if (errorString != null)
        {
            errorString = string.Format("第{0}行第{1}列的数据存在错误无法导出，", row + AppValues.DATA_FIELD_DATA_START_INDEX + 1, Utils.GetExcelColumnName(fieldInfo.ColumnSeq + 1)) + errorString;
            return null;
        }

        content.Append(value);
        // 一个字段结尾加逗号
        content.Append(",");

        return content.ToString();
    }

    private static string _GetNumberValue(FieldInfo fieldInfo, int row)
    {
        if (fieldInfo.Data[row] == null)
            return "null";
        else
            return fieldInfo.Data[row].ToString();
    }

    private static string _GetStringValue(FieldInfo fieldInfo, int row)
    {
        StringBuilder content = new StringBuilder();

        content.Append("\"");
        content.Append(fieldInfo.Data[row].ToString().Replace("\n", "\\n").Replace("\"", "\\\""));
        content.Append("\"");

        return content.ToString();
    }

    private static string _GetBoolValue(FieldInfo fieldInfo, int row)
    {
        if ((bool)fieldInfo.Data[row] == true)
            return "true";
        else
            return "false";
    }

    private static string _GetLangValue(FieldInfo fieldInfo, int row)
    {
        StringBuilder content = new StringBuilder();

        if (fieldInfo.Data[row] != null)
        {
            content.Append("\"");
            content.Append(fieldInfo.Data[row].ToString().Replace("\n", "\\n").Replace("\"", "\\\""));
            content.Append("\"");
        }
        else
        {
            if (AppValues.IsPrintEmptyStringWhenLangNotMatching == true)
                content.Append("\"\"");
            else
                content.Append("null");
        }

        return content.ToString();
    }

    private static string _GetDateValue(FieldInfo fieldInfo, int row)
    {
        StringBuilder content = new StringBuilder();

        DateFormatType dateFormatType = TableAnalyzeHelper.GetDateFormatType(fieldInfo.ExtraParam[AppValues.TABLE_INFO_EXTRA_PARAM_KEY_DATE_TO_LUA_FORMAT].ToString());
        string exportFormatString = null;
        // 若date型声明toLua的格式为dateTable，则按input格式进行导出
        if (dateFormatType == DateFormatType.DataTable)
        {
            dateFormatType = TableAnalyzeHelper.GetDateFormatType(fieldInfo.ExtraParam[AppValues.TABLE_INFO_EXTRA_PARAM_KEY_DATE_INPUT_FORMAT].ToString());
            exportFormatString = fieldInfo.ExtraParam[AppValues.TABLE_INFO_EXTRA_PARAM_KEY_DATE_INPUT_FORMAT].ToString();
        }
        else
            exportFormatString = fieldInfo.ExtraParam[AppValues.TABLE_INFO_EXTRA_PARAM_KEY_DATE_TO_LUA_FORMAT].ToString();

        switch (dateFormatType)
        {
            case DateFormatType.FormatString:
                {
                    if (fieldInfo.Data[row] == null)
                        content.Append("null");
                    else
                        content.Append("\"").Append(((DateTime)(fieldInfo.Data[row])).ToString(exportFormatString)).Append("\"");

                    break;
                }
            case DateFormatType.ReferenceDateSec:
                {
                    if (fieldInfo.Data[row] == null)
                        content.Append("null");
                    else
                        content.Append(((DateTime)(fieldInfo.Data[row]) - AppValues.REFERENCE_DATE_LOCAL).TotalSeconds);

                    break;
                }
            case DateFormatType.ReferenceDateMsec:
                {
                    if (fieldInfo.Data[row] == null)
                        content.Append("null");
                    else
                        content.Append(((DateTime)(fieldInfo.Data[row]) - AppValues.REFERENCE_DATE_LOCAL).TotalMilliseconds);

                    break;
                }
            default:
                {
                    Utils.LogErrorAndExit("错误：用_GetDateValue函数导出json文件的date型的DateFormatType非法");
                    break;
                }
        }

        return content.ToString();
    }

    private static string _GetTimeValue(FieldInfo fieldInfo, int row)
    {
        StringBuilder content = new StringBuilder();

        TimeFormatType timeFormatType = TableAnalyzeHelper.GetTimeFormatType(fieldInfo.ExtraParam[AppValues.TABLE_INFO_EXTRA_PARAM_KEY_TIME_TO_LUA_FORMAT].ToString());
        switch (timeFormatType)
        {
            case TimeFormatType.FormatString:
                {
                    if (fieldInfo.Data[row] == null)
                        content.Append("null");
                    else
                        content.Append("\"").Append(((DateTime)(fieldInfo.Data[row])).ToString(fieldInfo.ExtraParam[AppValues.TABLE_INFO_EXTRA_PARAM_KEY_TIME_TO_LUA_FORMAT].ToString())).Append("\"");

                    break;
                }
            case TimeFormatType.ReferenceTimeSec:
                {
                    if (fieldInfo.Data[row] == null)
                        content.Append("null");
                    else
                        content.Append(((DateTime)(fieldInfo.Data[row]) - AppValues.REFERENCE_DATE).TotalSeconds);

                    break;
                }
            default:
                {
                    Utils.LogErrorAndExit("错误：用_GetTimeValue函数导出json文件的time型的TimeFormatType非法");
                    break;
                }
        }

        return content.ToString();
    }

    private static string _GetJsonValue(FieldInfo fieldInfo, int row)
    {
        if (fieldInfo.Data[row] == null)
            return "null";
        else
        {
            //// 将json字符串进行格式整理，去除引号之外的所有空白字符
            //StringBuilder stringBuilder = new StringBuilder();
            //string inputJsonString = fieldInfo.JsonString[row];
            //bool isInQuotationMarks = false;
            //for (int i = 0; i < inputJsonString.Length; ++i)
            //{
            //    char c = inputJsonString[i];

            //    if (c == '"')
            //    {
            //        stringBuilder.Append('"');
            //        if (i > 0 && inputJsonString[i - 1] != '\\')
            //            isInQuotationMarks = !isInQuotationMarks;
            //    }
            //    else if (c == ' ')
            //    {
            //        if (isInQuotationMarks == true)
            //            stringBuilder.Append(' ');
            //    }
            //    else if (c != '\n' && c != '\r' && c != '\t')
            //        stringBuilder.Append(c);
            //}

            //return stringBuilder.ToString();

            return JsonMapper.ToJson(fieldInfo.Data[row]);
        }
    }

    private static string _GetMapStringValue(FieldInfo fieldInfo, int row)
    {
        if (fieldInfo.Data[row] == null)
            return "null";
        else
            return JsonMapper.ToJson(fieldInfo.Data[row]);
    }

    private static string _GetTableStringValue(FieldInfo fieldInfo, int row, out string errorString)
    {
        errorString = null;
        if (fieldInfo.Data[row] == null)
            return "null";

        StringBuilder content = new StringBuilder();
        string inputData = fieldInfo.Data[row].ToString();

        // tableString字符串中不允许出现英文引号、斜杠
        if (inputData.Contains("\"") || inputData.Contains("\\") || inputData.Contains("/"))
        {
            errorString = "tableString字符串中不允许出现英文引号、斜杠";
            return null;
        }

        // 若tableString的key为#seq，则生成json array，否则生成json object
        if (fieldInfo.TableStringFormatDefine.KeyDefine.KeyType == TableStringKeyType.Seq)
            content.Append("[");
        else
            content.Append("{");

        // 每组数据间用英文分号分隔
        string[] allDataString = inputData.Split(new char[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries);
        // 记录每组数据中的key值（转为字符串后的），不允许出现相同的key（key：每组数据中的key值， value：第几组数据，从0开始记）
        Dictionary<string, int> stringKeys = new Dictionary<string, int>();
        for (int i = 0; i < allDataString.Length; ++i)
        {
            // 根据key的格式定义生成key
            switch (fieldInfo.TableStringFormatDefine.KeyDefine.KeyType)
            {
                case TableStringKeyType.Seq:
                    break;
                case TableStringKeyType.DataInIndex:
                    {
                        string value = _GetDataInIndexType(fieldInfo.TableStringFormatDefine.KeyDefine.DataInIndexDefine, allDataString[i], out errorString);
                        if (errorString == null)
                        {
                            if (fieldInfo.TableStringFormatDefine.KeyDefine.DataInIndexDefine.DataType == DataType.Int || fieldInfo.TableStringFormatDefine.KeyDefine.DataInIndexDefine.DataType == DataType.Long)
                            {
                                // 检查key是否在该组数据中重复
                                if (stringKeys.ContainsKey(value))
                                    errorString = string.Format("第{0}组数据与第{1}组数据均为相同的key（{2}）", stringKeys[value] + 1, i + 1, value);
                                else
                                {
                                    stringKeys.Add(value, i);
                                    content.AppendFormat("\"{0}\"", value);
                                }
                            }
                            else if (fieldInfo.TableStringFormatDefine.KeyDefine.DataInIndexDefine.DataType == DataType.String)
                            {
                                // string型的key不允许为空或纯空格且必须符合变量名的规范
                                value = value.Trim();
                                if (TableCheckHelper.CheckFieldName(value, out errorString))
                                {
                                    // 检查key是否在该组数据中重复
                                    if (stringKeys.ContainsKey(value))
                                        errorString = string.Format("第{0}组数据与第{1}组数据均为相同的key（{2}）", stringKeys[value] + 1, i + 1, value);
                                    else
                                    {
                                        stringKeys.Add(value, i);
                                        content.AppendFormat("\"{0}\"", value);
                                    }
                                }
                                else
                                    errorString = "string型的key不符合变量名定义规范，" + errorString;
                            }
                            else
                            {
                                Utils.LogErrorAndExit("错误：用_GetTableStringValue函数导出非int、long或string型的key值");
                                return null;
                            }
                        }

                        content.Append(":");

                        break;
                    }
                default:
                    {
                        Utils.LogErrorAndExit("错误：用_GetTableStringValue函数导出未知类型的key");
                        return null;
                    }
            }
            if (errorString != null)
            {
                errorString = string.Format("tableString中第{0}组数据（{1}）的key数据存在错误，", i + 1, allDataString[i]) + errorString;
                return null;
            }

            // 根据value的格式定义生成value
            switch (fieldInfo.TableStringFormatDefine.ValueDefine.ValueType)
            {
                case TableStringValueType.True:
                    {
                        content.Append("true");
                        break;
                    }
                case TableStringValueType.DataInIndex:
                    {
                        string value = _GetDataInIndexType(fieldInfo.TableStringFormatDefine.ValueDefine.DataInIndexDefine, allDataString[i], out errorString);
                        if (errorString == null)
                        {
                            DataType dataType = fieldInfo.TableStringFormatDefine.ValueDefine.DataInIndexDefine.DataType;
                            if (dataType == DataType.String || dataType == DataType.Lang)
                                content.AppendFormat("\"{0}\"", value);
                            else
                                content.Append(value);
                        }

                        break;
                    }
                case TableStringValueType.Table:
                    {
                        content.Append("{");

                        // 依次输出table中定义的子元素
                        foreach (TableElementDefine elementDefine in fieldInfo.TableStringFormatDefine.ValueDefine.TableValueDefineList)
                        {
                            content.AppendFormat("\"{0}\"", elementDefine.KeyName);
                            content.Append(":");
                            string value = _GetDataInIndexType(elementDefine.DataInIndexDefine, allDataString[i], out errorString);
                            if (errorString == null)
                            {
                                if (elementDefine.DataInIndexDefine.DataType == DataType.String || elementDefine.DataInIndexDefine.DataType == DataType.Lang)
                                    content.AppendFormat("\"{0}\"", value);
                                else
                                    content.Append(value);
                            }
                            content.Append(",");
                        }

                        // 去掉最后一个子元素后多余的英文逗号
                        content.Remove(content.Length - 1, 1);
                        content.Append("}");

                        break;
                    }
                default:
                    {
                        Utils.LogErrorAndExit("错误：用_GetTableStringValue函数导出未知类型的value");
                        return null;
                    }
            }
            if (errorString != null)
            {
                errorString = string.Format("tableString中第{0}组数据（{1}）的value数据存在错误，", i + 1, allDataString[i]) + errorString;
                return null;
            }

            // 每组数据生成完毕后加逗号
            content.Append(",");
        }

        // 去掉最后一组后多余的英文逗号
        content.Remove(content.Length - 1, 1);
        if (fieldInfo.TableStringFormatDefine.KeyDefine.KeyType == TableStringKeyType.Seq)
            content.Append("]");
        else
            content.Append("}");

        return content.ToString();
    }

    /// <summary>
    /// 将形如#1(int)的数据定义解析为要输出的字符串
    /// </summary>
    private static string _GetDataInIndexType(DataInIndexDefine define, string oneDataString, out string errorString)
    {
        // 一组数据中的子元素用英文逗号分隔
        string[] allElementString = oneDataString.Trim().Split(new char[] { ',' }, System.StringSplitOptions.RemoveEmptyEntries);
        // 检查是否存在指定序号的数据
        if (allElementString.Length < define.DataIndex)
        {
            errorString = string.Format("解析#{0}({1})类型的数据错误，输入的数据中只有{2}个子元素", define.DataIndex, define.DataType.ToString(), allElementString.Length);
            return null;
        }
        // 检查是否为指定类型的合法数据
        string inputString = allElementString[define.DataIndex - 1];
        if (define.DataType != DataType.String)
            inputString = inputString.Trim();

        string value = _GetDataStringInTableString(inputString, define.DataType, out errorString);
        if (errorString != null)
        {
            errorString = string.Format("解析#{0}({1})类型的数据错误，", define.DataIndex, define.DataType.ToString()) + errorString;
            return null;
        }
        else
            return value;
    }

    /// <summary>
    /// 将tableString类型数据字符串中的某个所填数据转为需要输出的字符串
    /// </summary>
    private static string _GetDataStringInTableString(string inputData, DataType dataType, out string errorString)
    {
        string result = null;
        errorString = null;

        switch (dataType)
        {
            case DataType.Bool:
                {
                    if ("1".Equals(inputData) || "true".Equals(inputData, StringComparison.CurrentCultureIgnoreCase))
                        result = "true";
                    else if ("0".Equals(inputData) || "false".Equals(inputData, StringComparison.CurrentCultureIgnoreCase))
                        result = "false";
                    else
                        errorString = string.Format("输入的\"{0}\"不是合法的bool值，正确填写bool值方式为填1或true代表真，0或false代表假", inputData);

                    break;
                }
            case DataType.Int:
            case DataType.Long:
                {
                    long longValue;
                    bool isValid = long.TryParse(inputData, out longValue);
                    if (isValid)
                        result = longValue.ToString();
                    else
                        errorString = string.Format("输入的\"{0}\"不是合法的{1}类型的值", inputData, dataType);

                    break;
                }
            case DataType.Float:
                {
                    float floatValue;
                    bool isValid = float.TryParse(inputData, out floatValue);
                    if (isValid)
                        result = floatValue.ToString();
                    else
                        errorString = string.Format("输入的\"{0}\"不是合法的float类型的值", inputData);

                    break;
                }
            case DataType.String:
                {
                    result = inputData;
                    break;
                }
            case DataType.Lang:
                {
                    if (AppValues.LangData.ContainsKey(inputData))
                    {
                        string langValue = AppValues.LangData[inputData];
                        if (langValue.Contains("\"") || langValue.Contains("\\") || langValue.Contains("/") || langValue.Contains(",") || langValue.Contains(";"))
                            errorString = string.Format("tableString中的lang型数据中不允许出现英文引号、斜杠、逗号、分号，你输入的key（{0}）对应在lang文件中的值为\"{1}\"", inputData, langValue);
                        else
                            result = langValue;
                    }
                    else
                        errorString = string.Format("输入的lang型数据的key（{0}）在lang文件中找不到对应的value", inputData);

                    break;
                }
            default:
                {
                    Utils.LogErrorAndExit(string.Format("错误：用_GetDataInTableString函数解析了tableString中不支持的数据类型{0}", dataType));
                    break;
                }
        }

        return result;
    }

    private static string _GetDictValue(FieldInfo fieldInfo, int row, out string errorString)
    {
        StringBuilder content = new StringBuilder();

        // 如果该dict数据用-1标为无效，则赋值为null
        if ((bool)fieldInfo.Data[row] == false)
            content.Append("null");
        else
        {
            // dict生成json object
            content.Append("{");

            // 逐个对子元素进行生成
            foreach (FieldInfo childField in fieldInfo.ChildField)
            {
                string oneFieldString = _GetOneField(childField, row, out errorString);
                if (errorString != null)
                    return null;
                else
                    content.Append(oneFieldString);
            }

            // 去掉最后一个子元素末尾多余的英文逗号
            content.Remove(content.Length - 1, 1);

            content.Append("}");
        }

        errorString = null;
        return content.ToString();
    }

    private static string _GetArrayValue(FieldInfo fieldInfo, int row, out string errorString)
    {
        StringBuilder content = new StringBuilder();

        // 如果该array数据用-1标为无效，则赋值为null
        if ((bool)fieldInfo.Data[row] == false)
            content.Append("null");
        else
        {
            // array生成json array
            content.Append("[");

            // 逐个对子元素进行生成
            bool hasValidChild = false;
            foreach (FieldInfo childField in fieldInfo.ChildField)
            {
                string oneFieldString = _GetOneField(childField, row, out errorString);
                if (errorString != null)
                    return null;

                // json array中不允许null元素
                if (!"null,".Equals(oneFieldString))
                {
                    content.Append(oneFieldString);
                    hasValidChild = true;
                }
            }

            // 去掉最后一个子元素末尾多余的英文逗号
            if (hasValidChild == true)
                content.Remove(content.Length - 1, 1);

            content.Append("]");
        }

        errorString = null;
        return content.ToString();
    }

    /// <summary>
    /// 将紧凑型的json字符串整理为带缩进和换行的形式，需注意string型值中允许含有括号和\"
    /// </summary>
    private static string _FormatJson(string json)
    {
        StringBuilder stringBuilder = new StringBuilder();
        int level = 0;
        bool isInQuotationMarks = false;
        for (int i = 0; i < json.Length; ++i)
        {
            char c = json[i];

            if (c == '[' || c == '{')
            {
                stringBuilder.Append(c);
                if (isInQuotationMarks == false)
                {
                    stringBuilder.AppendLine();
                    ++level;
                    stringBuilder.Append(_GetJsonIndentation(level));
                }
            }
            else if (c == ']' || c == '}')
            {
                if (isInQuotationMarks == false)
                {
                    stringBuilder.AppendLine();
                    --level;
                    stringBuilder.Append(_GetJsonIndentation(level));
                }
                stringBuilder.Append(c);
            }
            else if (c == ',')
            {
                stringBuilder.Append(c);
                if (isInQuotationMarks == false)
                {
                    stringBuilder.AppendLine();
                    stringBuilder.Append(_GetJsonIndentation(level));
                }
            }
            else if (c == '"')
            {
                stringBuilder.Append('"');
                if (i > 0 && json[i - 1] != '\\')
                    isInQuotationMarks = !isInQuotationMarks;
            }
            else
                stringBuilder.Append(c);
        }

        return stringBuilder.ToString();
    }

    private static string _GetJsonIndentation(int level)
    {
        StringBuilder stringBuilder = new StringBuilder();
        for (int i = 0; i < level; ++i)
            stringBuilder.Append(_JSON_INDENTATION_STRING);

        return stringBuilder.ToString();
    }

    //将json字符串进行紧凑
    private static string _ToTightJsonString(string inputJsonString)
    {
        // 将json字符串进行格式整理，去除引号之外的所有空白字符
        StringBuilder stringBuilder = new StringBuilder();
        bool isInQuotationMarks = false;

        for (int i = 0; i < inputJsonString.Length; ++i)
        {
            char c = inputJsonString[i];

            if (c == '"')
            {
                stringBuilder.Append('"');
                if (i > 0 && inputJsonString[i - 1] != '\\')
                    isInQuotationMarks = !isInQuotationMarks;
            }
            else if (c == ' ')
            {
                if (isInQuotationMarks == true)
                    stringBuilder.Append(' ');
            }
            else if (c != '\n' && c != '\r' && c != '\t')
                stringBuilder.Append(c);
        }
        return stringBuilder.ToString();
    }
}
