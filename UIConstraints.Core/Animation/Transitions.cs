using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace UIConstraints.Core.Animation;

[Serializable]
[DisplayName("Interpolate")]
[Description("Moves the value smoothly to the new value, following the curve. For numbers, and types that can be added, subtracted and multiplied by a number (like vectors and colors).")]
public class InterpolateTransition : TimedTransition {
    [Description("How integers are rounded while they move.")]
    public IntegerRounding rounding = IntegerRounding.Nearest;

    public override bool CanAnimate(Type valueType) => ValueBlending.CanBlend(valueType);

    public override TransitionState CreateState(TransitionContext context) => new State(this, context);

    private class State : TimedTransitionState<InterpolateTransition> {
        private object from;
        private object to;

        public State(InterpolateTransition transition, TransitionContext context) : base(transition, context) { }

        protected override void BeginMoving(object from, object to) {
            this.from = from;
            this.to = to;
        }

        protected override object Sample(float curved, float progress, double elapsed) {
            return ValueBlending.Lerp(from, to, curved, context.valueType, transition.rounding);
        }
    }
}

[Serializable]
[DisplayName("Spring")]
[Description("Moves the value like a spring pulling it to the new value: fast at first, and it may overshoot and bounce. Changes while moving keep the momentum. Has no fixed duration.")]
public class SpringTransition : Transition {
    [Description("How strongly the spring pulls (higher is faster).")]
    public float stiffness = 180;

    [Description("How much the movement is slowed down. Twice the square root of the stiffness stops exactly without overshooting, less bounces.")]
    public float damping = 18;

    [Description("How close the value has to be to the target (and how slow) to stop.")]
    public float precision = 0.001f;

    public override bool CanAnimate(Type valueType) => ValueBlending.CanBlend(valueType);

    public override TransitionState CreateState(TransitionContext context) => new State(this, context);

    public override string GetSummary() {
        string summary = base.GetSummary();
        int extra = summary.IndexOf(" · ", StringComparison.Ordinal);
        string springSummary = $"{GetDisplayName(GetType())} · stiffness {stiffness:0.#} · damping {damping:0.#}";
        return extra < 0 ? springSummary : springSummary + summary.Substring(extra);
    }

    private class State : TransitionState {
        private const double MAX_STEP = 1 / 120.0;
        private const double MAX_DURATION = 10; // stops eventually, also for types without a magnitude (e.g. colors)

        private readonly SpringTransition transition;
        private object position; // working values (see ValueBlending.ToWorkingValue)
        private object velocity;
        private object target;
        private double lastElapsed;

        public State(SpringTransition transition, TransitionContext context) : base(transition, context) {
            this.transition = transition;
        }

        protected override void Begin(object from, object to) {
            position = ValueBlending.ToWorkingValue(from, context.valueType);
            target = ValueBlending.ToWorkingValue(to, context.valueType);
            velocity ??= ValueBlending.Scale(position, 0); // keeps the velocity of the previous movement
            lastElapsed = 0;
        }

        // the spring just pulls somewhere else, keeping its position, velocity and time
        protected override bool Retarget(object from, object to) {
            target = ValueBlending.ToWorkingValue(to, context.valueType);
            return true;
        }

        protected override object Sample(double elapsed, out bool finished) {
            // fixed small steps, so the spring behaves the same at any frame rate
            double remaining = elapsed - lastElapsed;
            lastElapsed = elapsed;
            while (remaining > 0) {
                double step = Math.Min(remaining, MAX_STEP);
                remaining -= step;
                object offset = ValueBlending.Subtract(position, target);
                object acceleration = ValueBlending.Subtract(ValueBlending.Scale(offset, -transition.stiffness), ValueBlending.Scale(velocity, transition.damping));
                velocity = ValueBlending.Add(velocity, ValueBlending.Scale(acceleration, step));
                position = ValueBlending.Add(position, ValueBlending.Scale(velocity, step));
            }

            double distance = ValueBlending.Magnitude(ValueBlending.Subtract(position, target));
            double speed = ValueBlending.Magnitude(velocity);
            finished = elapsed > MAX_DURATION || (distance <= transition.precision && speed <= transition.precision);
            if (finished) velocity = ValueBlending.Scale(velocity, 0);
            return ValueBlending.FromWorkingValue(position, context.valueType);
        }
    }
}

[Serializable]
[DisplayName("Numbers in Text")]
[Description("Counts the numbers in a text to their new values, keeping the rest of the text (e.g. '50 gold' to '42 gold'). Changes of anything but the numbers are shown right away.")]
public class NumbersInTextTransition : TimedTransition {
    // numbers with an optional sign and decimals, also with thousands separators (e.g. 1,000,000.5)
    private static readonly Regex NUMBER = new Regex(@"-?\d{1,3}(?:,\d{3})+(?:\.\d+)?|-?\d+(?:\.\d+)?");

    public NumbersInTextTransition() {
        fullDurationFrom = 100;
        duration = 0.6f;
    }

    public override bool CanAnimate(Type valueType) => valueType == typeof(string);

    public override TransitionState CreateState(TransitionContext context) => new State(this, context);

    // texts that differ in more than their numbers are shown right away
    public override float GetDuration(object from, object to, Type valueType) {
        return MatchNumbers((string)from, (string)to) == null ? 0 : base.GetDuration(from, to, valueType);
    }

    protected override double GetDistance(object from, object to, Type valueType) {
        List<(double from, double to)> numbers = MatchNumbers((string)from, (string)to);
        return numbers == null ? double.NaN : numbers.Select(n => Math.Abs(n.to - n.from)).DefaultIfEmpty(0).Max();
    }

    // the direction of the first number that changes
    protected override int GetDirection(object from, object to) {
        List<(double from, double to)> numbers = MatchNumbers((string)from, (string)to);
        if (numbers == null) return 0;
        foreach ((double start, double end) in numbers)
            if (start != end) return Math.Sign(end - start);
        return 0;
    }

    // the numbers of both texts in pairs, null if the texts differ in more than their numbers
    private static List<(double from, double to)> MatchNumbers(string from, string to) {
        if (from == null || to == null || NUMBER.Replace(from, "#") != NUMBER.Replace(to, "#")) return null;
        MatchCollection fromNumbers = NUMBER.Matches(from);
        MatchCollection toNumbers = NUMBER.Matches(to);
        return Enumerable.Range(0, toNumbers.Count).Select(i => (Parse(fromNumbers[i].Value), Parse(toNumbers[i].Value))).ToList();
    }

    private static double Parse(string number) => double.Parse(number.Replace(",", ""), CultureInfo.InvariantCulture);

    private class State : TimedTransitionState<NumbersInTextTransition> {
        private string to;
        private List<(double from, double to)> numbers; // null if the text changed in more than its numbers

        public State(NumbersInTextTransition transition, TransitionContext context) : base(transition, context) { }

        protected override void BeginMoving(object from, object to) {
            this.to = (string)to;
            numbers = MatchNumbers((string)from, this.to);
        }

        protected override object Sample(float curved, float progress, double elapsed) {
            if (numbers == null) return to;

            // the numbers keep the format of the new ones: their decimals, and whether they use thousands separators
            int index = 0;
            return NUMBER.Replace(to, match => {
                (double start, double end) = numbers[index++];
                int decimals = match.Value.Contains(".") ? match.Value.Length - match.Value.IndexOf('.') - 1 : 0;
                double value = Math.Round(start + (end - start) * curved, decimals, MidpointRounding.AwayFromZero);
                return value.ToString((match.Value.Contains(",") ? "N" : "F") + decimals, CultureInfo.InvariantCulture);
            });
        }
    }
}

// in which order the characters of a scrambled text settle on the new text
public enum ScrambleOrder {
    LeftToRight,
    RightToLeft,
    Random
}

[Serializable]
[DisplayName("Scramble")]
[Description("Shows random characters that settle into the new text one by one, like a decoding effect.")]
public class ScrambleTransition : TimedTransition {
    [Description("The random characters shown until a character settles.")]
    public string characters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789#$%&*+?";

    public ScrambleOrder order = ScrambleOrder.LeftToRight;

    [Description("Seconds until the random characters change.")]
    public float changeInterval = 0.05f;

    [Description("Whether spaces and line breaks of the new text stay as they are while scrambling.")]
    public bool keepWhitespace = true;

    public ScrambleTransition() {
        duration = 0.8f;
        curve = TransitionCurve.Linear();
    }

    public override bool CanAnimate(Type valueType) => valueType == typeof(string);

    public override TransitionState CreateState(TransitionContext context) => new State(this, context);

    private class State : TimedTransitionState<ScrambleTransition> {
        private static readonly Random SEEDS = new Random();

        private string from;
        private string to;
        private int[] settleRank; // when each position settles, in the order of the settings
        private int seed;

        public State(ScrambleTransition transition, TransitionContext context) : base(transition, context) { }

        protected override void BeginMoving(object from, object to) {
            this.from = (string)from ?? "";
            this.to = (string)to ?? "";
            seed = SEEDS.Next();

            int length = Math.Max(this.from.Length, this.to.Length);
            int[] positions = Enumerable.Range(0, length).ToArray();
            if (transition.order == ScrambleOrder.RightToLeft) Array.Reverse(positions);
            else if (transition.order == ScrambleOrder.Random) positions = positions.OrderBy(position => new Random(seed + position).Next()).ToArray();
            settleRank = new int[length];
            for (int rank = 0; rank < length; rank++) settleRank[positions[rank]] = rank;
        }

        protected override object Sample(float curved, float progress, double elapsed) {
            float share = Math.Max(0, Math.Min(1, curved));
            int length = (int)Math.Round(from.Length + (to.Length - from.Length) * share);
            int settled = (int)Math.Floor(share * settleRank.Length);
            int step = transition.changeInterval > 0 ? (int)(elapsed / transition.changeInterval) : (int)(elapsed * 1000);
            Random random = new Random(seed ^ (step * 7919));
            string characters = string.IsNullOrEmpty(transition.characters) ? "#" : transition.characters;

            StringBuilder text = new StringBuilder(Math.Max(length, to.Length));
            for (int i = 0; i < settleRank.Length; i++) {
                char randomCharacter = characters[random.Next(characters.Length)];
                if (settleRank[i] < settled) {
                    if (i < to.Length) text.Append(to[i]);
                }
                else if (i < length) {
                    bool whitespace = i < to.Length && char.IsWhiteSpace(to[i]) && transition.keepWhitespace;
                    text.Append(whitespace ? to[i] : randomCharacter);
                }
            }

            return text.ToString();
        }
    }
}

// how a typewriter gets from the old text to the new one
public enum TypewriterMode {
    KeepCommonStart, // deletes the old text back to where it starts to differ from the new one, then types the rest
    DeleteAll, // deletes the whole old text, then types the whole new one
    TypeOnly // types the new text from the start, without deleting the old one first
}

[Serializable]
[DisplayName("Typewriter")]
[Description("Deletes the old text character by character, then types the new one.")]
public class TypewriterTransition : TimedTransition {
    public TypewriterMode mode = TypewriterMode.KeepCommonStart;

    public TypewriterTransition() {
        duration = 1;
        fullDurationFrom = 40;
        curve = TransitionCurve.Linear();
    }

    public override bool CanAnimate(Type valueType) => valueType == typeof(string);

    public override TransitionState CreateState(TransitionContext context) => new State(this, context);

    // the number of characters deleted and typed
    protected override double GetDistance(object from, object to, Type valueType) {
        (int kept, int deleted, int typed) = Plan(mode, (string)from ?? "", (string)to ?? "");
        return deleted + typed;
    }

    private static (int kept, int deleted, int typed) Plan(TypewriterMode mode, string from, string to) {
        int kept = 0;
        if (mode == TypewriterMode.KeepCommonStart)
            while (kept < from.Length && kept < to.Length && from[kept] == to[kept]) kept++;
        return (kept, mode == TypewriterMode.TypeOnly ? 0 : from.Length - kept, to.Length - kept);
    }

    private class State : TimedTransitionState<TypewriterTransition> {
        private string from;
        private string to;
        private (int kept, int deleted, int typed) plan;

        public State(TypewriterTransition transition, TransitionContext context) : base(transition, context) { }

        protected override void BeginMoving(object from, object to) {
            this.from = (string)from ?? "";
            this.to = (string)to ?? "";
            plan = Plan(transition.mode, this.from, this.to);
        }

        protected override object Sample(float curved, float progress, double elapsed) {
            int steps = (int)Math.Floor(Math.Max(0, Math.Min(1, curved)) * (plan.deleted + plan.typed)); // a character is typed once its time has passed, at a steady pace
            if (steps < plan.deleted) return from.Substring(0, from.Length - steps);
            return to.Substring(0, plan.kept + steps - plan.deleted);
        }
    }
}

[Serializable]
[DisplayName("Switch")]
[Description("Changes to the new value at once, after the delay. For values that can't move gradually (like booleans, sprites or enums), or to delay a change.")]
public class SwitchTransition : Transition {
    public override bool CanAnimate(Type valueType) => true;

    public override TransitionState CreateState(TransitionContext context) => new State(this, context);

    private class State : TransitionState {
        private object to;

        public State(SwitchTransition transition, TransitionContext context) : base(transition, context) { }

        protected override void Begin(object from, object to) {
            this.to = to;
        }

        protected override object Sample(double elapsed, out bool finished) {
            finished = true;
            return to;
        }
    }
}
