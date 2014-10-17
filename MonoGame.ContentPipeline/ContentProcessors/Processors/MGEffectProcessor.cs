﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework.Content.Pipeline;
using Microsoft.Xna.Framework.Content.Pipeline.Graphics;
using Microsoft.Xna.Framework.Content.Pipeline.Processors;
using Microsoft.Xna.Framework.Graphics;
using TwoMGFX;
using System.IO;
using System.Collections;
using System.ComponentModel;

namespace MonoGameContentProcessors.Processors
{
    [ContentProcessor(DisplayName = "MonoGame Effect")]
    class MGEffectProcessor : EffectProcessor
    {
        private ShaderPrecision vertexFloat = ShaderPrecision.Medium;
        [DisplayName("Vertex Float Precision")]
        [DefaultValue(ShaderPrecision.Medium)]
        public ShaderPrecision VertexFloat
        {
            get { return this.vertexFloat; }
            set { this.vertexFloat = value; }
        }

        private ShaderPrecision vertexInt = ShaderPrecision.Medium;
        [DisplayName("Vertex Int Precision")]
        [DefaultValue(ShaderPrecision.Medium)]
        public ShaderPrecision VertexInt
        {
            get { return this.vertexInt; }
            set { this.vertexInt = value; }
        }

        private ShaderPrecision pixelFloat = ShaderPrecision.Medium;
        [DisplayName("Pixel Float Precision")]
        [DefaultValue(ShaderPrecision.Medium)]
        public ShaderPrecision PixelFloat
        {
            get { return this.pixelFloat; }
            set { this.pixelFloat = value; }
        }

        private ShaderPrecision pixelInt = ShaderPrecision.Medium;
        [DisplayName("Pixel Int Precision")]
        [DefaultValue(ShaderPrecision.Medium)]
        public ShaderPrecision PixelInt
        {
            get { return this.pixelInt; }
            set { this.pixelInt = value; }
        }

        public override CompiledEffectContent Process(EffectContent input, ContentProcessorContext context)
        {
            //System.Diagnostics.Debugger.Launch();

            // If this isn't a MonoGame platform then do the default processing.
            var platform = ContentHelper.GetMonoGamePlatform();
            if (platform == MonoGamePlatform.None)
                return base.Process(input, context);

            var options = new Options();
            options.SourceFile = input.Identity.SourceFilename;
            options.Profile = platform == MonoGamePlatform.Windows8 ? ShaderProfile.DirectX_11 : ShaderProfile.OpenGL;
            options.Debug = DebugMode == EffectProcessorDebugMode.Debug;
            options.OutputFile = context.OutputFilename;

            // Parse the MGFX file expanding includes, macros, and returning the techniques.
            ShaderInfo shaderInfo;
            try
            {
                shaderInfo = ShaderInfo.FromFile(options.SourceFile, options);
                shaderInfo.VertexFloatPrecision = vertexFloat;
                shaderInfo.VertexIntPrecision = vertexInt;
                shaderInfo.PixelFloatPrecision = pixelFloat;
                shaderInfo.PixelIntPrecision = pixelInt;

                foreach (var dep in shaderInfo.Dependencies)
                    context.AddDependency(dep);
            }
            catch (Exception ex)
            {
                // TODO: Extract good line numbers from mgfx parser!
                throw new InvalidContentException(ex.Message, input.Identity, ex);
            }

            // Create the effect object.
            EffectObject effect = null;
            var shaderErrorsAndWarnings = string.Empty;
            try
            {
                effect = EffectObject.CompileEffect(shaderInfo, out shaderErrorsAndWarnings);
            }
            catch (ShaderCompilerException)
            {
                throw ProcessErrorsAndWarnings(shaderErrorsAndWarnings, input, context);
            }

            // Write out the effect to a runtime format.
            CompiledEffectContent result;
            try
            {
                using (var stream = new MemoryStream())
                {
                    using (var writer = new BinaryWriter(stream))
                        effect.Write(writer, options);

                    result = new CompiledEffectContent(stream.GetBuffer());
                }
            }
            catch (Exception ex)
            {
                throw new InvalidContentException("Failed to serialize the effect!", input.Identity, ex);
            }

            return result;
        }

        private Exception ProcessErrorsAndWarnings(string errorsAndWarnings, EffectContent input, ContentProcessorContext context)
        {
            // Split the errors by lines.
            var errors = errorsAndWarnings.Split('\n');

            // Process each error line extracting the location and message information.
            for (var i = 0; i < errors.Length; i++)
            {
                // Skip blank lines.
                if (errors[i].StartsWith(Environment.NewLine))
                    break;

                // find some unique characters in the error string
                var openIndex = errors[i].IndexOf('(');
                var closeIndex = errors[i].IndexOf(')');

                // can't process the message if it has no line counter
                if (openIndex == -1 || closeIndex == -1)
                    continue;

                // find the error number, then move forward into the message
                var errorIndex = errors[i].IndexOf('X', closeIndex);
                if (errorIndex < 0)
                    return new InvalidContentException(errors[i], input.Identity);

                // trim out the data we need to feed the logger
                var fileName = errors[i].Remove(openIndex);
                var lineAndColumn = errors[i].Substring(openIndex + 1, closeIndex - openIndex - 1);
                var description = errors[i].Substring(errorIndex);

                // when the file name is not present, the error can be found in the root file
                if (string.IsNullOrEmpty(fileName))
                    fileName = input.Identity.SourceFilename;

                // ensure that the file data points toward the correct file
                var fileInfo = new FileInfo(fileName);
                if (!fileInfo.Exists)
                {
                    var parentFile = new FileInfo(input.Identity.SourceFilename);
                    fileInfo = new FileInfo(Path.Combine(parentFile.Directory.FullName, fileName));
                }
                fileName = fileInfo.FullName;

                // construct the temporary content identity and file the error or warning
                var identity = new ContentIdentity(fileName, input.Identity.SourceTool, lineAndColumn);
                if (errors[i].Contains("warning"))
                {
                    description = "A warning was generated when compiling the effect.\n" + description;
                    context.Logger.LogWarning(string.Empty, identity, description, string.Empty);
                }
                else if (errors[i].Contains("error"))
                {
                    description = "Unable to compile the effect.\n" + description;
                    return new InvalidContentException(description, identity);
                }
            }

            // if no exceptions were created in the above loop, generate a generic one here
            return new InvalidContentException(errorsAndWarnings, input.Identity);
        }

    }
}
