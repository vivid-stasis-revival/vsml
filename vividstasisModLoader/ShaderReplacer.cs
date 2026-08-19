using UndertaleModLib;
using UndertaleModLib.Models;
using UndertaleModLib.Scripting;

namespace vividstasisModLoader;

public class ShaderReplacer(UndertaleData data, string modDir)
{
    readonly string _importFolder = $"{modDir}/shaders";

    public bool Exist()
    {
        return Directory.Exists(_importFolder);
    }

    public void Execute()
    {
        if (!Exist()) return;

        var shadersToModify = Directory.GetDirectories(_importFolder).Select(x => Path.GetFileName(x));
        var shadersExisting = new List<string>();
        var shadersNonExist = new List<string>();
        var currentList = new List<string>();
        var res = "";

        foreach (var shaderName in shadersToModify)
        {
            currentList.Clear();
            foreach (var t in data.Shaders)
            {
                var x = t.Name.Content;
                res += x + "\n";
                currentList.Add(x);
            }
            if (data.Shaders.ByName(shaderName) != null)
            {
                data.Shaders.Remove(data.Shaders.ByName(shaderName));
                AddShader(shaderName);
                Reorganize<UndertaleShader>(data.Shaders, currentList);
            }
            else
                AddShader(shaderName);
        }
    }
    void ImportShader(UndertaleShader existing_shader)
    {
        var localImportDir = _importFolder + "/" + existing_shader.Name.Content + "/";
        if (File.Exists(localImportDir + "Type.txt"))
        {
            var shader_type = File.ReadAllText(localImportDir + "Type.txt");
            if (shader_type.Contains("GLSL_ES"))
                existing_shader.Type = UndertaleShader.ShaderType.GLSL_ES;
            else if (shader_type.Contains("GLSL"))
                existing_shader.Type = UndertaleShader.ShaderType.GLSL;
            else if (shader_type.Contains("HLSL9"))
                existing_shader.Type = UndertaleShader.ShaderType.HLSL9;
            else if (shader_type.Contains("HLSL11"))
                existing_shader.Type = UndertaleShader.ShaderType.HLSL11;
            else if (shader_type.Contains("PSSL"))
                existing_shader.Type = UndertaleShader.ShaderType.PSSL;
            else if (shader_type.Contains("Cg_PSVita"))
                existing_shader.Type = UndertaleShader.ShaderType.Cg_PSVita;
            else if (shader_type.Contains("Cg_PS3"))
                existing_shader.Type = UndertaleShader.ShaderType.Cg_PS3;
        }
        if (File.Exists(localImportDir + "GLSL_ES_Fragment.txt"))
            existing_shader.GLSL_ES_Fragment.Content = File.ReadAllText(localImportDir + "GLSL_ES_Fragment.txt");
        if (File.Exists(localImportDir + "GLSL_ES_Vertex.txt"))
            existing_shader.GLSL_ES_Vertex.Content = File.ReadAllText(localImportDir + "GLSL_ES_Vertex.txt");
        if (File.Exists(localImportDir + "GLSL_Fragment.txt"))
            existing_shader.GLSL_Fragment.Content = File.ReadAllText(localImportDir + "GLSL_Fragment.txt");
        if (File.Exists(localImportDir + "GLSL_Vertex.txt"))
            existing_shader.GLSL_Vertex.Content = File.ReadAllText(localImportDir + "GLSL_Vertex.txt");
        if (File.Exists(localImportDir + "HLSL9_Fragment.txt"))
            existing_shader.HLSL9_Fragment.Content = File.ReadAllText(localImportDir + "HLSL9_Fragment.txt");
        if (File.Exists(localImportDir + "HLSL9_Vertex.txt"))
            existing_shader.HLSL9_Vertex.Content = File.ReadAllText(localImportDir + "HLSL9_Vertex.txt");
        if (File.Exists(localImportDir + "HLSL11_VertexData.bin"))
        {
            existing_shader.HLSL11_VertexData ??= new UndertaleShader.UndertaleRawShaderData();
            existing_shader.HLSL11_VertexData.Data = File.ReadAllBytes(localImportDir + "HLSL11_VertexData.bin");
            existing_shader.HLSL11_VertexData.IsNull = false;
        }
        if (File.Exists(localImportDir + "HLSL11_PixelData.bin"))
        {
            existing_shader.HLSL11_PixelData ??= new UndertaleShader.UndertaleRawShaderData();
            existing_shader.HLSL11_PixelData.IsNull = false;
            existing_shader.HLSL11_PixelData.Data = File.ReadAllBytes(localImportDir + "HLSL11_PixelData.bin");
        }
        if (File.Exists(localImportDir + "PSSL_VertexData.bin"))
        {
            existing_shader.PSSL_VertexData ??= new UndertaleShader.UndertaleRawShaderData();
            existing_shader.PSSL_VertexData.IsNull = false;
            existing_shader.PSSL_VertexData.Data = File.ReadAllBytes(localImportDir + "PSSL_VertexData.bin");
        }
        if (File.Exists(localImportDir + "PSSL_PixelData.bin"))
        {
            existing_shader.PSSL_PixelData ??= new UndertaleShader.UndertaleRawShaderData();
            existing_shader.PSSL_PixelData.IsNull = false;
            existing_shader.PSSL_PixelData.Data = File.ReadAllBytes(localImportDir + "PSSL_PixelData.bin");
        }
        if (File.Exists(localImportDir + "Cg_PSVita_VertexData.bin"))
        {
            existing_shader.Cg_PSVita_VertexData ??= new UndertaleShader.UndertaleRawShaderData();
            existing_shader.Cg_PSVita_VertexData.IsNull = false;
            existing_shader.Cg_PSVita_VertexData.Data = File.ReadAllBytes(localImportDir + "Cg_PSVita_VertexData.bin");
        }
        if (File.Exists(localImportDir + "Cg_PSVita_PixelData.bin"))
        {
            existing_shader.Cg_PSVita_PixelData ??= new UndertaleShader.UndertaleRawShaderData();
            existing_shader.Cg_PSVita_PixelData.IsNull = false;
            existing_shader.Cg_PSVita_PixelData.Data = File.ReadAllBytes(localImportDir + "Cg_PSVita_PixelData.bin");
        }
        if (File.Exists(localImportDir + "Cg_PS3_VertexData.bin"))
        {
            existing_shader.Cg_PS3_VertexData ??= new UndertaleShader.UndertaleRawShaderData();
            existing_shader.Cg_PS3_VertexData.IsNull = false;
            existing_shader.Cg_PS3_VertexData.Data = File.ReadAllBytes(localImportDir + "Cg_PS3_VertexData.bin");
        }
        if (File.Exists(localImportDir + "Cg_PS3_PixelData.bin"))
        {
            existing_shader.Cg_PS3_PixelData ??= new UndertaleShader.UndertaleRawShaderData();
            existing_shader.Cg_PS3_PixelData.IsNull = false;
            existing_shader.Cg_PS3_PixelData.Data = File.ReadAllBytes(localImportDir + "Cg_PS3_PixelData.bin");
        }
    }

    void AddShader(string shader_name)
    {
        var new_shader = new UndertaleShader();
        new_shader.Name = data.Strings.MakeString(shader_name);
        var localImportDir = _importFolder + "/" + shader_name + "/";
        if (File.Exists(localImportDir + "Type.txt"))
        {
            var shader_type = File.ReadAllText(localImportDir + "Type.txt");
            if (shader_type.Contains("GLSL_ES"))
                new_shader.Type = UndertaleShader.ShaderType.GLSL_ES;
            else if (shader_type.Contains("GLSL"))
                new_shader.Type = UndertaleShader.ShaderType.GLSL;
            else if (shader_type.Contains("HLSL9"))
                new_shader.Type = UndertaleShader.ShaderType.HLSL9;
            else if (shader_type.Contains("HLSL11"))
                new_shader.Type = UndertaleShader.ShaderType.HLSL11;
            else if (shader_type.Contains("PSSL"))
                new_shader.Type = UndertaleShader.ShaderType.PSSL;
            else if (shader_type.Contains("Cg_PSVita"))
                new_shader.Type = UndertaleShader.ShaderType.Cg_PSVita;
            else if (shader_type.Contains("Cg_PS3"))
                new_shader.Type = UndertaleShader.ShaderType.Cg_PS3;
            else
                new_shader.Type = UndertaleShader.ShaderType.GLSL_ES;
        }
        else
            new_shader.Type = UndertaleShader.ShaderType.GLSL_ES;
        if (File.Exists(localImportDir + "GLSL_ES_Fragment.txt"))
            new_shader.GLSL_ES_Fragment = data.Strings.MakeString(File.ReadAllText(localImportDir + "GLSL_ES_Fragment.txt"));
        else
            new_shader.GLSL_ES_Fragment = data.Strings.MakeString("");
        if (File.Exists(localImportDir + "GLSL_ES_Vertex.txt"))
            new_shader.GLSL_ES_Vertex = data.Strings.MakeString(File.ReadAllText(localImportDir + "GLSL_ES_Vertex.txt"));
        else
            new_shader.GLSL_ES_Vertex = data.Strings.MakeString("");
        if (File.Exists(localImportDir + "GLSL_Fragment.txt"))
            new_shader.GLSL_Fragment = data.Strings.MakeString(File.ReadAllText(localImportDir + "GLSL_Fragment.txt"));
        else
            new_shader.GLSL_Fragment = data.Strings.MakeString("");
        if (File.Exists(localImportDir + "GLSL_Vertex.txt"))
            new_shader.GLSL_Vertex = data.Strings.MakeString(File.ReadAllText(localImportDir + "GLSL_Vertex.txt"));
        else
            new_shader.GLSL_Vertex = data.Strings.MakeString("");
        if (File.Exists(localImportDir + "HLSL9_Fragment.txt"))
            new_shader.HLSL9_Fragment = data.Strings.MakeString(File.ReadAllText(localImportDir + "HLSL9_Fragment.txt"));
        else
            new_shader.HLSL9_Fragment = data.Strings.MakeString("");
        if (File.Exists(localImportDir + "HLSL9_Vertex.txt"))
            new_shader.HLSL9_Vertex = data.Strings.MakeString(File.ReadAllText(localImportDir + "HLSL9_Vertex.txt"));
        else
            new_shader.HLSL9_Vertex = data.Strings.MakeString("");
        if (File.Exists(localImportDir + "HLSL11_VertexData.bin"))
        {
            new_shader.HLSL11_VertexData = new UndertaleShader.UndertaleRawShaderData();
            new_shader.HLSL11_VertexData.Data = File.ReadAllBytes(localImportDir + "HLSL11_VertexData.bin");
            new_shader.HLSL11_VertexData.IsNull = false;
        }
        if (File.Exists(localImportDir + "HLSL11_PixelData.bin"))
        {
            new_shader.HLSL11_PixelData = new UndertaleShader.UndertaleRawShaderData();
            new_shader.HLSL11_PixelData.IsNull = false;
            new_shader.HLSL11_PixelData.Data = File.ReadAllBytes(localImportDir + "HLSL11_PixelData.bin");
        }
        if (File.Exists(localImportDir + "PSSL_VertexData.bin"))
        {
            new_shader.PSSL_VertexData = new UndertaleShader.UndertaleRawShaderData();
            new_shader.PSSL_VertexData.IsNull = false;
            new_shader.PSSL_VertexData.Data = File.ReadAllBytes(localImportDir + "PSSL_VertexData.bin");
        }
        if (File.Exists(localImportDir + "PSSL_PixelData.bin"))
        {
            new_shader.PSSL_PixelData = new UndertaleShader.UndertaleRawShaderData();
            new_shader.PSSL_PixelData.IsNull = false;
            new_shader.PSSL_PixelData.Data = File.ReadAllBytes(localImportDir + "PSSL_PixelData.bin");
        }
        if (File.Exists(localImportDir + "Cg_PSVita_VertexData.bin"))
        {
            new_shader.Cg_PSVita_VertexData = new UndertaleShader.UndertaleRawShaderData();
            new_shader.Cg_PSVita_VertexData.IsNull = false;
            new_shader.Cg_PSVita_VertexData.Data = File.ReadAllBytes(localImportDir + "Cg_PSVita_VertexData.bin");
        }
        if (File.Exists(localImportDir + "Cg_PSVita_PixelData.bin"))
        {
            new_shader.Cg_PSVita_PixelData = new UndertaleShader.UndertaleRawShaderData();
            new_shader.Cg_PSVita_PixelData.IsNull = false;
            new_shader.Cg_PSVita_PixelData.Data = File.ReadAllBytes(localImportDir + "Cg_PSVita_PixelData.bin");
        }
        if (File.Exists(localImportDir + "Cg_PS3_VertexData.bin"))
        {
            new_shader.Cg_PS3_VertexData = new UndertaleShader.UndertaleRawShaderData();
            new_shader.Cg_PS3_VertexData.IsNull = false;
            new_shader.Cg_PS3_VertexData.Data = File.ReadAllBytes(localImportDir + "Cg_PS3_VertexData.bin");
        }
        if (File.Exists(localImportDir + "Cg_PS3_PixelData.bin"))
        {
            new_shader.Cg_PS3_PixelData = new UndertaleShader.UndertaleRawShaderData();
            new_shader.Cg_PS3_PixelData.IsNull = false;
            new_shader.Cg_PS3_PixelData.Data = File.ReadAllBytes(localImportDir + "Cg_PS3_PixelData.bin");
        }
        if (File.Exists(localImportDir + "VertexShaderAttributes.txt"))
        {
            string line;
            // Read the file and display it line by line.
            StreamReader file = new StreamReader(localImportDir + "VertexShaderAttributes.txt");
            while ((line = file.ReadLine()) != null)
            {
                line = line.Trim();
                if (line != "")
                {
                    UndertaleShader.VertexShaderAttribute vertex_x = new UndertaleShader.VertexShaderAttribute();
                    vertex_x.Name = data.Strings.MakeString(line);
                    new_shader.VertexShaderAttributes.Add(vertex_x);
                }
            }
            file.Close();
        }
        data.Shaders.Add(new_shader);
    }

    void Reorganize<T>(IList<T> list, List<string> order) where T : UndertaleNamedResource, new()
    {
        var temp = new Dictionary<string, T>();
        foreach (var asset in list)
        {
            var assetName = asset.Name?.Content;
            if (order.Contains(assetName))
            {
                temp[assetName] = asset;
            }
        }

        var addOrder = new List<T>();
        for (var i = order.Count - 1; i >= 0; i--)
        {
            T asset;
            try
            {
                asset = temp[order[i]];
            }
            catch (Exception e)
            {
                throw new Exception("Missing asset with name \"" + order[i] + "\"");
            }
            addOrder.Add(asset);
        }

        foreach (T asset in addOrder)
            list.Remove(asset);
        foreach (T asset in addOrder)
            list.Insert(0, asset);
    }
}