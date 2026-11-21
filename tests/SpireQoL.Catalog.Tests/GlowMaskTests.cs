using SpireQoL.Cards;
using Xunit;

namespace SpireQoL.Catalog.Tests;

/// <summary>
/// Decision #127: the card glow's shader gets the mask added (its alpha cut where the see-through card covers it). Checked on the
/// game's card glow shader (shaders/card_ripple.gdshader, v0.113.1: blend_add, no vertex(), the "width" NCardHighlight tweens) and
/// on the shapes the edit must handle: an existing vertex(), nested braces, comments with braces.
/// </summary>
public class GlowMaskTests
{
    private const string Ripple = """
        shader_type canvas_item;

        render_mode blend_add;

        uniform float ease;
        uniform float modulo_width;

        uniform float width;
        uniform float ripple_speed;

        void fragment() {
        	float sdf_alpha = mod(TIME * ripple_speed + COLOR.a, modulo_width);
            float front_ease = smoothstep(0, ease, sdf_alpha);
            float easeAmount = front_ease * modulo_width;
            float brightness = COLOR.a - sdf_alpha + easeAmount;

            COLOR.a = smoothstep(1.0 - width, width+(1.0 - width), brightness);
        }
        """;

    [Fact]
    public void TheGlowShaderGetsTheMaskAfterItsOwnWork()
    {
        var code = GlowMask.MaskedCode(Ripple)!;
        Assert.NotNull(code);
        // Declarations after render_mode, before the uniforms; a vertex() that keeps the glow's local position.
        Assert.True(code.IndexOf("render_mode blend_add;", StringComparison.Ordinal) < code.IndexOf("uniform sampler2D spireqol_mask", StringComparison.Ordinal));
        Assert.True(code.IndexOf("varying vec2 spireqol_local;", StringComparison.Ordinal) < code.IndexOf("uniform float ease;", StringComparison.Ordinal));
        Assert.Contains("void vertex() {", code);
        Assert.Contains("spireqol_local = VERTEX;", code);
        // The cut comes after the glow's own COLOR.a, inside fragment().
        var own = code.IndexOf("COLOR.a = smoothstep(1.0 - width", StringComparison.Ordinal);
        var cut = code.IndexOf("COLOR.a *= 1.0 - smoothstep(0.5, 0.9, texture(spireqol_mask", StringComparison.Ordinal);
        Assert.True(own >= 0 && cut > own);
        Assert.True(cut < code.IndexOf("void vertex()", StringComparison.Ordinal));
        // Braces still balance.
        Assert.Equal(code.Count(character => character == '{'), code.Count(character => character == '}'));
    }

    [Fact]
    public void AnExistingVertexAndNestedBracesAndCommentsAreHandled()
    {
        const string shader = """
            shader_type canvas_item;
            uniform float width = 1.0;
            void vertex() {
                VERTEX += UV * width;
            }
            void fragment() {
                // a { brace in a comment
                if (COLOR.a > 0.5) { COLOR.a = 1.0; } else { COLOR.a = 0.0; }
                /* another } in a block comment */
            }
            """;
        var code = GlowMask.MaskedCode(shader)!;
        Assert.NotNull(code);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(code, @"void\s+vertex"));
        Assert.True(code.IndexOf("spireqol_local = VERTEX;", StringComparison.Ordinal) < code.IndexOf("VERTEX += UV * width;", StringComparison.Ordinal));
        Assert.True(code.IndexOf("COLOR.a *= 1.0 - smoothstep", StringComparison.Ordinal) > code.IndexOf("another } in a block comment", StringComparison.Ordinal));
        Assert.EndsWith("}", code.TrimEnd());
    }

    [Theory]
    [InlineData("shader_type spatial;\nvoid fragment() { ALBEDO = vec3(1.0); }")]
    [InlineData("shader_type canvas_item;\nvoid vertex() { }")]
    [InlineData("")]
    public void ShadersItCantExtendAreLeftAlone(string shader) => Assert.Null(GlowMask.MaskedCode(shader));
}
