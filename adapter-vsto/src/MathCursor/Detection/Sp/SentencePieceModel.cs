using System;
using System.Collections.Generic;
using System.IO;
using Google.Protobuf;

namespace MathCursor.Detection.Sp
{
    /// <summary>
    /// Représente un modèle SentencePiece chargé depuis un fichier .bpe.model
    /// (qui contient en réalité du Unigram pour XLM-RoBERTa, malgré le nom).
    /// Parse le protobuf via Google.Protobuf.CodedInputStream, en lisant chaque
    /// sous-message comme un ByteString puis en le re-parsant (ReadBytes
    /// fonctionne pour les sous-messages car wire format identique).
    /// </summary>
    public sealed class SentencePieceModel
    {
        public sealed class Piece
        {
            public string Text { get; set; } = "";
            public float Score { get; set; }
            public byte Type { get; set; } // 1=NORMAL, 2=UNKNOWN, 3=CONTROL, 4=USER_DEFINED, 6=BYTE
        }

        public IReadOnlyList<Piece> Pieces { get; }

        // SentencePiece-internal IDs (positions dans la liste Pieces)
        public int SpUnkId { get; }
        public int SpBosId { get; }
        public int SpEosId { get; }

        public bool EscapeWhitespaces { get; }
        public bool AddDummyPrefix { get; }
        public bool RemoveExtraWhitespaces { get; }
        public bool TreatWhitespaceAsSuffix { get; }

        private SentencePieceModel(
            List<Piece> pieces, int unkId, int bosId, int eosId,
            bool escapeWs, bool addDummy, bool removeExtraWs, bool treatWsAsSuffix)
        {
            Pieces = pieces;
            SpUnkId = unkId;
            SpBosId = bosId;
            SpEosId = eosId;
            EscapeWhitespaces = escapeWs;
            AddDummyPrefix = addDummy;
            RemoveExtraWhitespaces = removeExtraWs;
            TreatWhitespaceAsSuffix = treatWsAsSuffix;
        }

        public static SentencePieceModel LoadFromFile(string path)
        {
            var bytes = File.ReadAllBytes(path);
            var coded = new CodedInputStream(bytes);

            var pieces = new List<Piece>();
            int unkId = 0, bosId = 1, eosId = 2;
            bool escapeWs = true, addDummy = true, removeExtraWs = false, treatWsAsSuffix = false;

            // ModelProto fields :
            //   1 = repeated SentencePiece pieces
            //   2 = TrainerSpec trainer_spec
            //   3 = NormalizerSpec normalizer_spec
            while (!coded.IsAtEnd)
            {
                uint tag = coded.ReadTag();
                int field = (int)(tag >> 3);

                if (field == 1)
                {
                    pieces.Add(ParsePiece(ReadSubBytes(coded)));
                }
                else if (field == 2)
                {
                    ParseTrainerSpec(ReadSubBytes(coded), ref unkId, ref bosId, ref eosId, ref treatWsAsSuffix);
                }
                else if (field == 3)
                {
                    ParseNormalizerSpec(ReadSubBytes(coded), ref escapeWs, ref addDummy, ref removeExtraWs);
                }
                else
                {
                    coded.SkipLastField();
                }
            }

            return new SentencePieceModel(pieces, unkId, bosId, eosId, escapeWs, addDummy, removeExtraWs, treatWsAsSuffix);
        }

        // ReadBytes() lit un champ length-delimited (string/bytes/nested-message,
        // même wire format) et renvoie les octets du body. Permet de re-parser
        // récursivement les sous-messages sans avoir besoin de PushLimit.
        private static byte[] ReadSubBytes(CodedInputStream coded) =>
            coded.ReadBytes().ToByteArray();

        // SentencePiece message :
        //   1 = string piece
        //   2 = float score
        //   3 = enum type
        private static Piece ParsePiece(byte[] subBytes)
        {
            var coded = new CodedInputStream(subBytes);
            var p = new Piece { Type = 1 };
            while (!coded.IsAtEnd)
            {
                uint tag = coded.ReadTag();
                int field = (int)(tag >> 3);
                switch (field)
                {
                    case 1: p.Text = coded.ReadString(); break;
                    case 2: p.Score = coded.ReadFloat(); break;
                    case 3: p.Type = (byte)coded.ReadInt32(); break;
                    default: coded.SkipLastField(); break;
                }
            }
            return p;
        }

        // TrainerSpec (champs utiles, cf. sentencepiece_model.proto officiel) :
        //   24 = bool treat_whitespace_as_suffix
        //   40 = int32 unk_id
        //   41 = int32 bos_id
        //   42 = int32 eos_id
        private static void ParseTrainerSpec(byte[] subBytes, ref int unkId, ref int bosId, ref int eosId, ref bool treatWsAsSuffix)
        {
            var coded = new CodedInputStream(subBytes);
            while (!coded.IsAtEnd)
            {
                uint tag = coded.ReadTag();
                int field = (int)(tag >> 3);
                switch (field)
                {
                    case 24: treatWsAsSuffix = coded.ReadBool(); break;
                    case 40: unkId = coded.ReadInt32(); break;
                    case 41: bosId = coded.ReadInt32(); break;
                    case 42: eosId = coded.ReadInt32(); break;
                    default: coded.SkipLastField(); break;
                }
            }
        }

        // NormalizerSpec (cf. sentencepiece_model.proto officiel) :
        //   1 = string name
        //   2 = bytes precompiled_charsmap   ← peut faire plusieurs Mo, à skip
        //   3 = bool add_dummy_prefix
        //   4 = bool remove_extra_whitespaces
        //   5 = bool escape_whitespaces
        //   6 = string normalization_rule_tsv
        private static void ParseNormalizerSpec(byte[] subBytes, ref bool escapeWs, ref bool addDummy, ref bool removeExtraWs)
        {
            var coded = new CodedInputStream(subBytes);
            while (!coded.IsAtEnd)
            {
                uint tag = coded.ReadTag();
                int field = (int)(tag >> 3);
                switch (field)
                {
                    case 3: addDummy = coded.ReadBool(); break;
                    case 4: removeExtraWs = coded.ReadBool(); break;
                    case 5: escapeWs = coded.ReadBool(); break;
                    default: coded.SkipLastField(); break;
                }
            }
        }
    }
}
