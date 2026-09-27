export enum RpgMaterialType {
  Unclassified = 0,
  CoreManual = 1,
  Manual = 2,
  Adventure = 3,
  Supplement = 4,
  OtherPublication = 5,
  Map = 6,
  CharacterSheet = 7,
  GameAid = 8,
  CardsAndTokens = 9,
  OtherResource = 10,
}

export const RPG_PUBLICATION_TYPES: readonly RpgMaterialType[] = [
  RpgMaterialType.CoreManual,
  RpgMaterialType.Manual,
  RpgMaterialType.Adventure,
  RpgMaterialType.Supplement,
  RpgMaterialType.OtherPublication,
];

export const RPG_RESOURCE_TYPES: readonly RpgMaterialType[] = [
  RpgMaterialType.Map,
  RpgMaterialType.CharacterSheet,
  RpgMaterialType.GameAid,
  RpgMaterialType.CardsAndTokens,
  RpgMaterialType.OtherResource,
];
