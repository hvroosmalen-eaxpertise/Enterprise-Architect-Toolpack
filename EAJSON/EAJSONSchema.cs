﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TSF_EA = TSF.UmlToolingFramework.Wrappers.EA;
using Newtonsoft.Json.Schema;
using UML = TSF.UmlToolingFramework.UML;
using Newtonsoft.Json.Linq;

namespace EAJSON
{
    public class EAJSONSchema
    {
        private TSF_EA.ElementWrapper _rootElement;

        private TSF_EA.ElementWrapper rootElement
        {
            get => _rootElement;
            set
            {
                if (value != null && 
                    value.stereotypes.Any(x => x.name.Equals("JSONSchema", StringComparison.InvariantCultureIgnoreCase)))
                {
                    _rootElement = value;
                }
                else
                {
                    throw new ArgumentException("The root element should have the «JSONSchema» stereotype");
                }
            }
        }
        public EAJSONSchema(TSF_EA.ElementWrapper rootElement)
        {
            this.rootElement = rootElement;
        }
        private Uri _schemaId;
        public Uri schemaId
        {
            get
            {
                if (this._schemaId == null)
                {
                    var idTag = this.rootElement.taggedValues.FirstOrDefault(x => x.name.Equals("id", StringComparison.InvariantCultureIgnoreCase));
                    this._schemaId = new Uri(idTag?.tagValue.ToString());
                }
                return this._schemaId;
            }
        }
        private Uri _schemaVersion;
        public Uri schemaVersion
        {
            get
            {
                if (this._schemaVersion == null)
                {
                    var schemaTag = this.rootElement.taggedValues.FirstOrDefault(x => x.name.Equals("schema", StringComparison.InvariantCultureIgnoreCase));
                    this._schemaVersion = new Uri(schemaTag?.tagValue.ToString());
                }
                return this._schemaVersion;
            }
        }
        private JSchema _schema;
        public JSchema schema
        {
            get
            {
                if (_schema == null)
                {
                    _schema = this.generateSchema();
                }
                return _schema;
            }
        }
        private JObject _definitions;
        private JObject definitions
        {
            get
            {
                if (_definitions == null)
                {
                    _definitions = new JObject();
                }
                return _definitions;
            }
        }

        private JSchema generateSchema()
        {

            //create schema
            var generatedSchema = createSchemaForElement(this.rootElement as UML.Classes.Kernel.Type);
            //set version and Id
            generatedSchema.SchemaVersion = this.schemaVersion;
            generatedSchema.Id = this.schemaId;
            generatedSchema.ExtensionData.Add("definitions", this.definitions);

            return generatedSchema;
        }
        private JSchema createSchemaForElement(UML.Classes.Kernel.Type type)
        {
            var elementSchema = new JSchema();
            //set schema type
            setSchemaType(type, elementSchema);
            //add properties
            var element = type as TSF_EA.ElementWrapper;
            if (element != null)
            {
                addProperties(elementSchema, element);
            }
            return elementSchema;
        }

        private void addProperties(JSchema schema, TSF_EA.ElementWrapper element)
        {
            //don't do anything if there are no attributes
            if (!element.attributes.Any()) return;
            //loop attributes
            foreach (var attribute in element.attributes)
            {
                //get the type of the attribute
                schema.Properties.Add(attribute.name, getPropertySchema(attribute));
                //add to required list if mandatory
                if (attribute.lower > 0 )
                {
                    schema.Required.Add(attribute.name);
                }
            }
            //don't allow additional properties
            schema.AllowAdditionalProperties = false;
        }
        private JSchema getPropertySchema(UML.Classes.Kernel.Property attribute)
        {
            var typeSchema = new JSchema();
            if (attribute.upper.isUnlimited || attribute.upper.integerValue > 1)
            {
                typeSchema.Type = JSchemaType.Array;
                //set lower value
                if (attribute.lower > 0)
                {
                    typeSchema.MinimumItems = attribute.lower;
                }
                //set upper value
                if (!attribute.upper.isUnlimited)
                {
                    typeSchema.MaximumItems = attribute.upper.integerValue;
                }
                //set the type of the items
                var itemsType = createSchemaForElement(attribute.type);
                //add schema to definitions if of type object
                if (itemsType.Type == JSchemaType.Object)
                {
                    this.definitions.Add(attribute.type.name, itemsType);
                }
                typeSchema.Items.Add(itemsType);
            }
            else
            {
                setSchemaType(attribute.type, typeSchema);
            }
            //process facets on the attribute
            processFacets(attribute, typeSchema);
            
            return typeSchema;
        }

        private static void setSchemaType(UML.Classes.Kernel.Type type, JSchema typeSchema)
        {
            //TODO find a better, non hardcoded way to determine the type properties
            if (type is UML.Classes.Kernel.Class)
            {
                typeSchema.Type = JSchemaType.Object;
            }
            else if (type is UML.Classes.Kernel.Enumeration)
            {
                typeSchema.Type = JSchemaType.String;
                foreach(var enumValue in ((UML.Classes.Kernel.Enumeration)type).ownedLiterals)
                {
                    typeSchema.Enum.Add(JValue.CreateString(enumValue.name));
                }   
            }
            else if (type is UML.Classes.Kernel.DataType)
            {
                switch (type.name.ToLower())
                {
                    //check if the name is in the list of JSON types
                    case "string":
                        typeSchema.Type = JSchemaType.String;
                        break;
                    case "number":
                        typeSchema.Type = JSchemaType.Number;
                        break;
                    case "boolean":
                        typeSchema.Type = JSchemaType.Boolean;
                        break;
                    //check if the name is one of the known XML types
                    case "decimal":
                    case "float":
                    case "double":
                        typeSchema.Type = JSchemaType.Number;
                        break;
                    case "duration":
                        typeSchema.Type = JSchemaType.String;
                        typeSchema.Pattern = @"^-?P((([0-9]+Y([0-9]+M)?([0-9]+D)?|([0-9]+M)([0-9]+D)?|([0-9]+D))(T(([0-9]+H)([0-9]+M)?([0-9]+(\.[0-9]+)?S)?|([0-9]+M)([0-9]+(\.[0-9]+)?S)?|([0-9]+(\.[0-9]+)?S)))?)|(T(([0-9]+H)([0-9]+M)?([0-9]+(\.[0-9]+)?S)?|([0-9]+M)([0-9]+(\.[0-9]+)?S)?|([0-9]+(\.[0-9]+)?S))))$";
                        break;
                    case "datetime":
                        typeSchema.Type = JSchemaType.String;
                        typeSchema.Format = "date-time";
                        break;
                    //TODO: find better way
                    //case "time":
                    //case "date":
                    //case "gyearmonth":
                    //case "gyear":
                    //case "gmonthday":
                    //case "gday":
                    //case "gmonth":
                    //case "hexbinary":
                    //case "base64binary":
                    //case "anyuri":
                    //case "qname":
                    //case "notation":
                    //case "normalizedstring":
                    //case "token":
                    //case "language":
                    //case "nmtoken":
                    //case "nmtokens":
                    //case "name":
                    //case "ncname":
                    //case "id":
                    //case "idref":
                    //case "idrefs":
                    //case "entity":
                    //case "entities":
                    case "integer":
                        typeSchema.Type = JSchemaType.Integer;
                        break;
                    case "nonpositiveinteger":
                        typeSchema.Type = JSchemaType.Integer;
                        typeSchema.Maximum = 0;
                        break;
                    case "negativeinteger":
                        typeSchema.Type = JSchemaType.Integer;
                        typeSchema.Maximum = 0;
                        typeSchema.ExclusiveMaximum = true;
                        break;
                    case "long":
                        typeSchema.Type = JSchemaType.Integer;
                        break;
                    case "int":
                        typeSchema.Type = JSchemaType.Integer;
                        break;
                    case "short":
                        typeSchema.Type = JSchemaType.Integer;
                        break;
                    case "byte":
                        typeSchema.Type = JSchemaType.Integer;
                        break;
                    case "nonnegativeinteger":
                    case "unsignedlong":
                    case "unsignedint":
                    case "unsignedshort":
                    case "unsignedbyte":
                    case "positiveinteger":
                        typeSchema.Type = JSchemaType.Integer;
                        typeSchema.Minimum = 0;
                        break;
                        //TODO
                        //case "yearmonthduration":
                        //case "daytimeduration":
                        //case "datetimestamp":
                }
                //do base types
                if (!typeSchema.Type.HasValue)
                {
                    if (((TSF_EA.DataType)type).superClasses.Any())
                    {
                        setSchemaType(((TSF_EA.DataType)type).superClasses.First(), typeSchema);
                    }
                }
                //process facets (of the datatype
                processFacets(type, typeSchema);
            }

        }
        private static void processFacets(UML.Classes.Kernel.Element element, JSchema typeSchema)
        {
            double? totalDigits = null;
            double? fractionDigits = null;
            bool minimumSet = false;
            bool maximumSet = false;
            bool multipleOfSet = false;
            foreach (var tag in element.taggedValues)
            {
                //get string value
                var stringValue = tag.tagValue.ToString();
                //get long value
                long tempLong;
                long? longValue = null;
                if (long.TryParse(stringValue, out tempLong))
                {
                    longValue = tempLong;
                }
                //get double value
                double tempDouble;
                double? doubleValue = null;
                if (double.TryParse(stringValue, out tempDouble))
                {
                    doubleValue = tempDouble;
                }

                switch (tag.name.ToLower())
                {
                    
                    //string facets
                    case "minlength":
                        typeSchema.MinimumLength = longValue;
                        break;
                    case "maxlength":
                        typeSchema.MaximumLength = longValue;
                        break;
                    case "pattern":
                        if (!string.IsNullOrEmpty(stringValue))
                        {
                            //initialize
                            typeSchema.Pattern = string.Empty;
                            //add start indicator
                            if (!stringValue.StartsWith("^"))
                            {
                                typeSchema.Pattern = "^";
                            }
                            //add pattern
                            typeSchema.Pattern = typeSchema.Pattern + stringValue;
                            //add end indicator
                            if (!stringValue.EndsWith("$"))
                            {
                                typeSchema.Pattern = typeSchema.Pattern + "$";
                            }
                        }
                        break;
                    case "format":
                        if (!string.IsNullOrEmpty(stringValue))
                        {
                            typeSchema.Format = stringValue;
                        }
                        break;
                    //numeric facets
                    case "minimum":
                        typeSchema.Minimum = longValue;
                        minimumSet = true;
                        break;
                    case "exclusiveminimum":
                        typeSchema.Minimum = longValue;
                        typeSchema.ExclusiveMinimum = true;
                        minimumSet = true;
                        break;
                    case "maximum":
                        typeSchema.Maximum = longValue;
                        maximumSet = true;
                        break;
                    case "exclusivemaximum":
                        typeSchema.Maximum = longValue;
                        typeSchema.ExclusiveMaximum = true;
                        maximumSet = true;
                        break;
                    case "multipleof":
                        typeSchema.MultipleOf = doubleValue;
                        multipleOfSet = true;
                        break;
                    //xsd facets
                    case "fractiondigits":
                        fractionDigits = doubleValue;
                        break;
                    case "length":
                        typeSchema.MinimumLength = longValue;
                        typeSchema.MaximumLength = longValue;
                        break;
                    case "maxexclusive":
                        typeSchema.Maximum = longValue;
                        typeSchema.ExclusiveMaximum = true;
                        maximumSet = true;
                        break;
                    case "maxinclusive":
                        typeSchema.Maximum = longValue;
                        maximumSet = true;
                        break;
                    case "minexclusive":
                        typeSchema.Minimum = longValue;
                        typeSchema.ExclusiveMinimum = true;
                        minimumSet = true;
                        break;
                    case "mininclusive":
                        typeSchema.Minimum = longValue;
                        minimumSet = true;
                        break;
                    case "totaldigits":
                        totalDigits = doubleValue;
                        break;
                }
            }
            //set minimum and maximum and multipleOf based on fractiondigits and totalDigits
            //but only if minimum, maximum and multipleOf are not yet set
            if (totalDigits.HasValue)
            {
                if (!fractionDigits.HasValue)
                {
                    fractionDigits = 0;
                }
                if (!maximumSet)
                {
                    typeSchema.Maximum = Math.Pow(10, totalDigits.Value - fractionDigits.Value);
                    typeSchema.ExclusiveMaximum = true;
                }
                if (!multipleOfSet && fractionDigits > 0)
                {
                    typeSchema.MultipleOf = Math.Pow(10, (fractionDigits.Value * -1));
                }
                if (!minimumSet)
                {
                    typeSchema.Minimum = Math.Pow(10, totalDigits.Value - fractionDigits.Value) * -1;
                    typeSchema.ExclusiveMinimum = true;
                }
            }
        }
    }
}
