import {Chapter} from './chapter';
import {HourEstimateRange} from './series-detail/hour-estimate-range';
import {IHasCover} from "./common/i-has-cover";
import {IHasReadingTime} from "./common/i-has-reading-time";
import {IHasProgress} from "./common/i-has-progress";
import {IHasMetadataIds} from "./common/i-has-metadata-ids";
import {DriveThruRpgMatchStatus, RpgGeekMatchStatus, RpgMaterialType} from "./rpg/rpg-catalog";

export interface Volume extends IHasCover, IHasReadingTime, IHasProgress, IHasMetadataIds {
  id: number;
  seriesId: number;
  minNumber: number;
  maxNumber: number;
  name: string;
  createdUtc: string;
  lastModifiedUtc: string;
  pages: number;
  pagesRead: number;
  wordCount: number;
  chapters: Array<Chapter>;
  /**
   * This is only available on the object when fetched for SeriesDetail
   */
  timeEstimate?: HourEstimateRange;
  minHoursToRead: number;
  maxHoursToRead: number;
  avgHoursToRead: number;

  coverImage?: string;
  coverImageLocked: boolean;
  primaryColor: string;
  secondaryColor: string;

  aniListId: number;
  malId: number;
  hardcoverId: number;
  metronId: number;
  comicVineId: string | null;
  mangaBakaId: number;
  cbrId: number;
  rpgMaterialType: RpgMaterialType;
  summary: string;
  nameLocked: boolean;
  summaryLocked: boolean;
  rpgPublicationYear: number | null;
  rpgPublicationYearLocked: boolean;
  rpgWriters: string[];
  rpgWritersLocked: boolean;
  rpgPublishers: string[];
  rpgPublishersLocked: boolean;
  rpgGeekId: number | null;
  rpgGeekMatchStatus: RpgGeekMatchStatus;
  rpgGeekLastCheckedUtc: string | null;
  driveThruRpgId: number | null;
  driveThruRpgMatchStatus: DriveThruRpgMatchStatus;
  driveThruRpgLastCheckedUtc: string | null;
}
