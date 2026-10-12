namespace RockGame
{
    /// <summary>The gameshow wand's questions: multiple choice, A to D, not too easy (NetGame.Wacky.cs picks one; the
    /// one who was zapped answers; Hud.Wacky.cs shows it to everyone).</summary>
    public static class Trivia
    {
        // the question, its four answers, and which one is right (0 = A ... 3 = D)
        static readonly (string q, string a, string b, string c, string d, int right)[] k_Q =
        {
            ("Which planet has the most moons?", "Jupiter", "Saturn", "Uranus", "Neptune", 1),
            ("What is the hardest natural substance?", "Quartz", "Titanium", "Diamond", "Graphene", 2),
            ("How many hearts does an octopus have?", "One", "Two", "Three", "Four", 2),
            ("Which element has the chemical symbol 'K'?", "Krypton", "Potassium", "Calcium", "Phosphorus", 1),
            ("What is the capital city of Australia?", "Sydney", "Melbourne", "Perth", "Canberra", 3),
            ("In what year did the Titanic sink?", "1905", "1912", "1918", "1923", 1),
            ("Which is the longest river in the world (by most measures)?", "Amazon", "Yangtze", "Nile", "Mississippi", 2),
            ("How many bones are in an adult human body?", "186", "206", "226", "246", 1),
            ("What gas do plants mostly take in from the air?", "Oxygen", "Nitrogen", "Carbon dioxide", "Hydrogen", 2),
            ("Who painted 'The Starry Night'?", "Claude Monet", "Vincent van Gogh", "Pablo Picasso", "Salvador Dali", 1),
            ("What is the smallest prime number?", "0", "1", "2", "3", 2),
            ("Which ocean is the largest?", "Atlantic", "Indian", "Arctic", "Pacific", 3),
            ("What is the speed of light, roughly?", "300,000 km/s", "30,000 km/s", "3,000,000 km/s", "150,000 km/s", 0),
            ("Which country gave the Statue of Liberty to the USA?", "Britain", "Spain", "France", "Italy", 2),
            ("How many players are on a football (soccer) team on the pitch?", "9", "10", "11", "12", 2),
            ("What is the largest organ of the human body?", "Liver", "Skin", "Brain", "Lungs", 1),
            ("Which planet spins on its side?", "Uranus", "Venus", "Mars", "Mercury", 0),
            ("Which language has the most native speakers?", "English", "Spanish", "Hindi", "Mandarin Chinese", 3),
            ("What is the freezing point of water in Fahrenheit?", "0", "32", "100", "-40", 1),
            ("Which animal is the largest ever known to have lived?", "Blue whale", "Argentinosaurus", "Megalodon", "Woolly mammoth", 0),
            ("Who wrote 'Romeo and Juliet'?", "Charles Dickens", "William Shakespeare", "Jane Austen", "Mark Twain", 1),
            ("What is the main ingredient of guacamole?", "Tomato", "Lime", "Avocado", "Pepper", 2),
            ("How many sides does a dodecagon have?", "10", "12", "14", "20", 1),
            ("What is the tallest mountain on Earth above sea level?", "K2", "Kilimanjaro", "Everest", "Denali", 2),
            ("Which metal is liquid at room temperature?", "Mercury", "Lead", "Sodium", "Tin", 0),
            ("In which country were the Olympic Games first held?", "Italy", "Greece", "Egypt", "France", 1),
            ("Which blood type is the universal donor?", "AB+", "A-", "O-", "B+", 2),
            ("What's the closest star to Earth?", "Proxima Centauri", "Sirius", "The Sun", "Betelgeuse", 2),
            ("Which is NOT a primary colour of light?", "Red", "Green", "Blue", "Yellow", 3),
            ("How many minutes are in a full day?", "1,440", "1,240", "2,400", "1,140", 0),
            ("What is the capital of Canada?", "Toronto", "Vancouver", "Ottawa", "Montreal", 2),
            ("Which planet is the hottest?", "Mercury", "Venus", "Mars", "Jupiter", 1),
            ("What do bees collect to make honey?", "Pollen", "Nectar", "Sap", "Dew", 1),
            ("Which video game character is a plumber?", "Sonic", "Link", "Mario", "Kirby", 2),
            ("How many keys does a standard piano have?", "76", "88", "92", "100", 1),
            ("What is H2O2 better known as?", "Water", "Salt", "Hydrogen peroxide", "Ammonia", 2),
            ("Which animal can sleep standing up AND has no gall bladder?", "Cow", "Horse", "Giraffe", "Elephant", 1),
            ("What's the largest desert in the world?", "Sahara", "Gobi", "Antarctica", "Arabian", 2),
            ("Who was the first person to walk on the Moon?", "Buzz Aldrin", "Yuri Gagarin", "Neil Armstrong", "John Glenn", 2),
            ("Which shape has the most sides?", "Hexagon", "Octagon", "Heptagon", "Nonagon", 3),
            ("What is the currency of Japan?", "Yuan", "Won", "Yen", "Ringgit", 2),
            ("What does DNA stand for?", "Deoxyribonucleic acid", "Dinitrogen acid", "Dual nucleic acid", "Deoxyribose amino", 0),
            ("Which bird is known for copying sounds, even chainsaws?", "Parrot", "Lyrebird", "Crow", "Mockingjay", 1),
            ("How many time zones does Russia have?", "7", "9", "11", "13", 2),
            ("Which gas makes up most of Earth's air?", "Oxygen", "Nitrogen", "Argon", "Carbon dioxide", 1),
            ("What's the square root of 144?", "11", "12", "13", "14", 1),
            ("Which country has the most islands?", "Indonesia", "Philippines", "Sweden", "Canada", 2),
            ("What is the fastest land animal?", "Lion", "Pronghorn", "Cheetah", "Greyhound", 2),
            ("What colour is a polar bear's skin?", "White", "Pink", "Black", "Grey", 2),
            ("In Minecraft, what do you need to make a Nether portal?", "Obsidian", "Bedrock", "End stone", "Netherrack", 0),
            ("Which is the smallest planet in our solar system?", "Mars", "Pluto", "Mercury", "Venus", 2),
            ("How many legs does a spider have?", "6", "8", "10", "12", 1),
            ("What year did World War II end?", "1943", "1944", "1945", "1946", 2),
            ("Which organ makes insulin?", "Liver", "Pancreas", "Kidney", "Stomach", 1),
            ("What's the longest bone in the human body?", "Spine", "Humerus", "Femur", "Tibia", 2),
            ("How long does sunlight take to reach Earth?", "About 8 minutes", "About 8 hours", "About 8 seconds", "About 1 hour", 0),
            ("What's 15% of 200?", "15", "20", "30", "35", 2),
            ("Which of these is a mammal?", "Shark", "Dolphin", "Salmon", "Octopus", 1),
            ("Which instrument has 47 strings and pedals?", "Cello", "Harp", "Piano", "Sitar", 1),
            ("What is the boiling point of water at sea level in Kelvin?", "273 K", "300 K", "373 K", "212 K", 2),
            ("Which continent has the most countries?", "Asia", "Europe", "Africa", "South America", 2),
            ("Which planet is known as the Red Planet?", "Jupiter", "Mars", "Saturn", "Mercury", 1),
            ("What's the main language of Brazil?", "Spanish", "Portuguese", "French", "Brazilian", 1),
            ("How many degrees are in the angles of a triangle, added up?", "90", "180", "270", "360", 1),
        };

        public static int Count => k_Q.Length;
        public static string Question(int i) => k_Q[Wrap(i)].q;
        public static string Answer(int i, int which)
        {
            var e = k_Q[Wrap(i)];
            return which == 0 ? e.a : which == 1 ? e.b : which == 2 ? e.c : e.d;
        }
        public static int Correct(int i) => k_Q[Wrap(i)].right;
        static int Wrap(int i) => ((i % k_Q.Length) + k_Q.Length) % k_Q.Length;
    }
}
