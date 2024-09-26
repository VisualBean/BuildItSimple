using System.Reflection;


namespace BuildItSimple.JsonDeserializer
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var json =  """
                        { 
                            "name": "John \"Escapee\" Doe", 
                            "age": 30, 
                            "isAdmin": true, 
                            "allergies": [ "lactose", "hard work" ]
                        }
                        """;
            var person = JsonDeserializer.DeserializeJsonString<Person>(json);
            Console.WriteLine(person.Name);
        }
    }
    public class Person
    {
        public string Name { get; set; }

        public int Age { get; set; }

        public bool isAdmin { get; set; }

        public List<string> Allergies { get; set; }
    }
    public static class JsonDeserializer
    {
        public static T DeserializeJsonString<T>(string jsonStr) where T : class, new()
        {
            var tokens = TokenizeJson(jsonStr);
            if (tokens[0].Type != TokenType.StartObject || tokens[tokens.Count - 1].Type != TokenType.EndObject)
            {
                throw new ArgumentException("Invalid JSON string");
            }

            return ParseTokens<T>(tokens);
        }

        private static T ParseTokens<T>(IList<Token> tokens)
        {
            var obj = Activator.CreateInstance(typeof(T));
            bool inArray = false;
            List<object> currentArray = null;
            PropertyInfo currentProperty = null;
            for (int i = 1; i < tokens.Count - 1; i++)
            {
                switch (tokens[i].Type)
                {
                    case TokenType.PropertyName:
                        currentProperty = GetPropertyInfo(typeof(T), tokens[i].Value);
                        break;
                    case TokenType.StartArray:
                        inArray = true;
                        currentArray = new List<object>();
                        break;
                    case TokenType.EndArray:
                        if (currentProperty != null)
                        {
                            SetEnumerableProperty(obj, currentProperty, currentArray);
                            inArray = false;
                            currentArray = null;
                            currentProperty = null;
                        }
                        
                        break;
                    
                    default:
                        if (currentProperty != null) // We have a property to set.
                        {
                            if (inArray)
                            {
                                currentArray.Add(ConvertTokenValue(tokens[i])); // Add to our list.
                            }
                            else
                            {
                                currentProperty.SetValue(obj, ConvertTokenValue(tokens[i])); // Set prioperty value.
                                currentProperty = null; // Reset current property.
                            }
                        }
                        break;
                }
            }

            return (T)obj;
        }

        private static void SetEnumerableProperty(object obj, PropertyInfo propertyInfo, List<object> arrayValues)
        {
            var elementType = propertyInfo.PropertyType.GetGenericArguments()[0]; // Get the type of elements in the IEnumerable<T>.

            var listType = typeof(List<>).MakeGenericType(elementType); // Create a new instance of List<T> with the correct element type.
            var listInstance = Activator.CreateInstance(listType); // Create an empty list instance.

            foreach (var value in arrayValues)
            {
                if (value != null)
                {
                    ((System.Collections.IList)listInstance).Add(value);
                }
                    
            }

            propertyInfo.SetValue(obj, listInstance);
        }

        private static PropertyInfo GetPropertyInfo(Type type, string propertyName)
        {
            var properties = type.GetProperties();

            if (propertyName != null)
            {
                foreach (var property in properties)
                {
                    if (string.Equals(property.Name, propertyName, StringComparison.InvariantCultureIgnoreCase))
                    {
                        return property;
                    }
                }
            }

            // property not found, skip.
            return null;
        }

        private static object ConvertTokenValue(Token token)
        {
            switch (token.Type)
            {
                case TokenType.BooleanValue:
                    return Convert.ToBoolean(token.Value);
                case TokenType.IntegerValue:
                    return Convert.ToInt32(token.Value);
                default:
                    return token.Value;
            }
        }

        public static List<Token> TokenizeJson(string jsonString)
        {
            var tokens = new List<Token>();
            bool inQuotes = false;
            string currentBuffer = "";
            bool isPropertyName = true;
            bool isEscaped = false;
            bool inArray = false; // Added flag to indicate if we are currently inside an array.

            for (int i = 0; i < jsonString.Length; i++)
            {
                char c = jsonString[i];

                switch (c)
                {
                    case '{': // Object Start
                        tokens.Add(new Token(TokenType.StartObject, string.Empty));
                        break;
                    case '}': // Object End
                        if (!string.IsNullOrWhiteSpace(currentBuffer))
                        {
                            var value = ParseValue(currentBuffer);
                            tokens.Add(value);
                            currentBuffer = "";
                        }
                        tokens.Add(new Token(TokenType.EndObject, string.Empty));
                        break;
                    case '"': // Handle quotes
                        if (!inQuotes)// Start Quote.
                        {
                            inQuotes = true;
                            currentBuffer = "";
                        }
                        else// End Quote.
                        {
                            inQuotes = false;
                            if (isPropertyName) // Check if the current buffer contains a property name or value.
                            {
                                tokens.Add(new Token(TokenType.PropertyName, currentBuffer));
                                isPropertyName = false;
                                currentBuffer = "";
                            }
                            else
                            {
                                tokens.Add(new Token(TokenType.StringValue, currentBuffer));
                                if (!inArray)
                                {
                                    isPropertyName = true; // Reset the flag for the next property name or value.
                                }
                                currentBuffer = "";
                            }
                        }
                        break;
                    case '\\': // Handle escape
                        if (inQuotes)
                        {
                            // if we find an escape, we add the next character to the buffer and move the cursor along.
                            currentBuffer += jsonString[i + 1];
                            i++;
                        }
                        break;
                    case '[': // Array Start
                        inArray = true; // Set the flag to indicate that we are inside an array.
                        tokens.Add(new Token(TokenType.StartArray, string.Empty));
                        break;
                    case ']': // Array End
                        if (!string.IsNullOrWhiteSpace(currentBuffer))
                        {
                            var value = ParseValue(currentBuffer);
                            tokens.Add(value);
                            currentBuffer = "";
                        }
                        inArray = false; // Reset the flag to indicate that we are outside an array.
                        tokens.Add(new Token(TokenType.EndArray, string.Empty));
                        break;
                    case ',':
                        if (!inQuotes)
                        {
                            if (!isPropertyName && !string.IsNullOrWhiteSpace(currentBuffer)) // Check if the current buffer contains a property value or an array value.
                            {
                                var value = ParseValue(currentBuffer);
                                tokens.Add(value);
                                if (!inArray)
                                {
                                    isPropertyName = true; // Reset the flag for the next property name or value.
                                }
                                currentBuffer = "";
                            }
                        }
                        break;
                    case ':': // Handle property value separator.
                        if (!inQuotes)
                        {
                            isPropertyName = false; // Indicate that the next buffer contains a property value.
                            currentBuffer = "";
                        }
                        break;
                    default:
                        currentBuffer += c;
                        break;
                }
            }

            return tokens;
        }



        public static Token ParseValue(string buffer)
        {
            if (buffer.Trim() == "true" || buffer.Trim() == "false")
            {
                return new Token(TokenType.BooleanValue, buffer.Trim());
            }
            else if (int.TryParse(buffer, out _))
            {
                return new Token(TokenType.IntegerValue, buffer.Trim());
            }

            return new Token(TokenType.StringValue, buffer);
        }




        public enum TokenType
        {
            StartObject,
            EndObject,
            StartArray,
            EndArray,
            PropertyName,
            StringValue,
            IntegerValue,
            BooleanValue,
        }

        public class Token
        {
            public TokenType Type { get; set; }
            public string Value { get; set; }


            public Token(TokenType type, string value)
            {
                Type = type;
                Value = value;
            }
        }
}
    
}
